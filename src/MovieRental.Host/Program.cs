using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MovieRental.Host.Infrastructure;
using MovieRental.Host.Infrastructure.Localization;
using MovieRental.Host.Middleware;
using MovieRental.Host.Pages;
using MovieRental.Modules.Catalog;
using MovieRental.Modules.Cinema;
using MovieRental.Modules.Identity;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Media;
using MovieRental.Modules.Rentals;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Cqrs.Behaviors;
using MovieRental.SharedKernel.Modules;
using MovieRental.SharedKernel.Security;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Composition. Program.cs knows the list of modules and nothing about their insides.
// ---------------------------------------------------------------------------
builder.Services.AddModules(
    builder.Configuration,
    new IdentityModule(),
    new CatalogModule(),
    new RentalsModule(),
    new CinemaModule(),
    new MediaModule());

builder.Services.AddScoped<IDispatcher, Dispatcher>();
builder.Services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
builder.Services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddSingleton<Translations>();
builder.Services.AddScoped<ILanguageContext, LanguageContext>();
builder.Services.AddScoped<IPageShellFactory, PageShellFactory>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ---------------------------------------------------------------------------
// Security
//
// Two schemes, one identity. The API runs on bearer tokens; Razor and the Swagger page are
// plain browser navigations that carry no Authorization header, so they read an HttpOnly
// cookie issued at login. The selector below picks whichever the request actually brought.
// ---------------------------------------------------------------------------
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
          ?? throw new InvalidOperationException("The Jwt configuration section is missing.");

// The value in appsettings.json is a placeholder, and that file is committed. Signing real
// tokens with it means anyone who has read the repository can mint one for any account, so
// the app refuses to start on it anywhere but a developer's machine.
if (!builder.Environment.IsDevelopment() &&
    jwt.SecretKey.Contains("change", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Jwt:SecretKey is still the placeholder from appsettings.json. Set a real one: " +
        "dotnet user-secrets set \"Jwt:SecretKey\" \"<64 random characters>\"");
}

if (jwt.SecretKey.Length < 32)
    throw new InvalidOperationException("Jwt:SecretKey must be at least 32 characters. Use user secrets, not appsettings.json.");

const string SmartScheme = "smart";

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = SmartScheme;
        options.DefaultChallengeScheme = SmartScheme;
    })
    .AddPolicyScheme(SmartScheme, SmartScheme, options =>
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal)
            || IsHubTokenRequest(context.Request)
                ? JwtBearerDefaults.AuthenticationScheme
                : CookieAuthenticationDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = TokenService.SigningKey(jwt.SecretKey),
            // Default is five minutes of slack, which quietly extends every token's life.
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // A browser cannot set headers on a WebSocket upgrade, so SignalR sends the token as
        // ?access_token=. It is read only on the hub path: accepting tokens from the query
        // string everywhere would put them in every server log and browser history entry.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (IsHubTokenRequest(context.Request))
                    context.Token = context.Request.Query["access_token"];
                return Task.CompletedTask;
            }
        };
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "rr.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.LoginPath = "/account";
        options.AccessDeniedPath = "/account";

        // Pages redirect to sign-in; the API must not. An API call without a token used to
        // get a 302 to the HTML login page, which fetch follows silently — the caller then got
        // a 200 full of HTML and failed parsing it, instead of a plain 401 it could act on.
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs"))
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            else
                context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs"))
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            else
                context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AppRoles.Admin, policy => policy.RequireRole(AppRoles.Admin))
    .AddPolicy(AppPolicies.SecurityDesk, policy => policy.RequireRole(AppRoles.Security, AppRoles.Admin))
    .AddPolicy(AppPolicies.BackOffice, policy => policy.RequireRole(AppRoles.Cashier, AppRoles.Admin))
    .AddPolicy(AppRoles.Customer, policy => policy.RequireAuthenticatedUser());

static bool IsHubTokenRequest(HttpRequest request) =>
    request.Path.StartsWithSegments("/hubs") && !string.IsNullOrEmpty(request.Query["access_token"]);

// ---------------------------------------------------------------------------
// Rate limiting
//
// A six-digit code is a million guesses. Five attempts burn the code, but nothing stopped
// somebody requesting a fresh one and starting again, so the attempt counter alone was not
// a defence. These limits are per client address and apply to the endpoints where guessing
// is the attack: signing in, and anything that takes a code.
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, ct) =>
    {
        // Say how long to wait rather than leaving the caller to guess. A legitimate user
        // who mistyped their password twice deserves a straight answer.
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);

        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            """{"code":"too_many_requests","message":"Too many attempts. Wait a minute and try again."}""", ct);
    };

    options.AddPolicy(AppPolicies.AuthRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(http),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(1) }));

    options.AddPolicy(AppPolicies.CodeRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(http),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(5) }));

    // Behind a proxy the socket address is the proxy's, so the forwarded header is used when
    // present. Configure ForwardedHeaders before trusting it in production.
    static string ClientKey(HttpContext http) =>
        http.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
        ?? http.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
});

// ---------------------------------------------------------------------------
// Presentation
// ---------------------------------------------------------------------------
builder.Services.AddPublicApiCaching();
builder.Services.AddRazorPages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "WatchingYou API", Version = "v1" });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token returned by /api/auth/login.",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };

    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = [] });
});

// Cross-origin callers. The website itself is served by this host and needs none of this.
//  - The phone app (Capacitor) loads its screens from the device: capacitor://localhost on iOS,
//    https://localhost on Android. It calls this API by full URL, in every environment.
//  - The Vite dev servers (website 5173, app 5174) run on their own origins, in Development only.
// Named origins only, never "any": the API accepts credentials, and a wildcard would let any
// site make signed-in calls on a visitor's behalf.
const string ClientCors = "clients";
var corsOrigins = (builder.Configuration.GetSection("Mobile:AllowedOrigins").Get<string[]>()
                   ?? ["capacitor://localhost", "https://localhost", "http://localhost"]).ToList();
if (builder.Environment.IsDevelopment())
{
    corsOrigins.Add(builder.Configuration["Frontend:DevServerUrl"] ?? "http://localhost:5173");
    corsOrigins.Add("http://localhost:5174");
}
builder.Services.AddCors(options => options.AddPolicy(ClientCors, policy => policy
    .WithOrigins([.. corsOrigins])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// Compression for the static bundle only: scripts, styles and SVG shrink three to five times.
// Pages and API responses are left alone on purpose — compressing a response that mixes a
// secret (a session, a token) with text an attacker can influence is what BREACH exploits.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ["text/javascript", "application/javascript", "text/css", "image/svg+xml"];
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(o =>
    o.Level = System.IO.Compression.CompressionLevel.Fastest);

var app = builder.Build();

// ---------------------------------------------------------------------------
// Pipeline. Order matters: exceptions first so everything below is covered, and
// authentication before the Swagger gate so it has an identity to check.
// ---------------------------------------------------------------------------
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors(ClientCors);

if (app.Environment.IsDevelopment())
{
    await DevelopmentDatabaseBootstrapper.InitialiseAsync(app.Services);
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();

// A missing page produces no exception, so the middleware above never sees it. Re-executing
// into the error page keeps 404 and 403 looking like part of the site rather than the
// server's default blank response. Scoped away from /api, because a fetch expecting JSON
// should not be handed a page of HTML to parse. The SignalR hub likewise: its client reads
// the status (401 means "refresh the token and reconnect"), and re-executing it into the
// error page turned a 401 into a 404.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/hubs"),
    branch => branch.UseStatusCodePagesWithReExecute("/error/{0}"));

// The web-app manifest is served with its proper type, which install prompts check for.
var staticTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
staticTypes.Mappings[".webmanifest"] = "application/manifest+json";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = staticTypes,
    // The bundle is served without a version query (see _Layout.cshtml), so the browser is
    // told to revalidate it on every load. With an ETag that is a 304 and no download when
    // nothing changed, and the new build the moment something did.
    OnPrepareResponse = context =>
    {
        if (context.Context.Request.Path.StartsWithSegments("/app"))
            context.Context.Response.Headers.CacheControl = "no-cache";

        // The service worker must always be checked for updates, or a fix to it could take a
        // day to reach people who already have the old one.
        if (context.Context.Request.Path.Equals("/sw.js"))
        {
            context.Context.Response.Headers.CacheControl = "no-cache";
            context.Context.Response.Headers["Service-Worker-Allowed"] = "/";
        }
    }
});
app.UseAuthentication();
app.UseAuthorization();

// The front door. Someone with no session who opens one of the public pages is shown the
// welcome page first, and brought back to where they were going once signed in. Pages that
// already require an account keep their own sign-in redirect; the API, the hub, files, the
// account and privacy pages and the phone app's calls are never touched.
string[] welcomeGated = ["/", "/on-display", "/ai-catalog", "/human-craft", "/cinema", "/pro", "/help"];
app.Use(async (context, next) =>
{
    var request = context.Request;
    var path = request.Path.Value?.TrimEnd('/') is { Length: > 0 } trimmed ? trimmed : "/";
    if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && context.User.Identity?.IsAuthenticated != true
        && welcomeGated.Contains(path, StringComparer.OrdinalIgnoreCase))
    {
        var back = request.Path + request.QueryString;
        context.Response.Redirect("/welcome?returnUrl=" + Uri.EscapeDataString(back));
        return;
    }
    await next();
});

app.UseRateLimiter();
app.UseOutputCache();
app.EvictOnWrite();

ApiReference.Map(app);

app.MapRazorPages();
app.MapModules();
AnalyticsEndpoints.Map(app);
LanguageEndpoints.Map(app);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }))
   .WithTags("System").AllowAnonymous();

app.Run();
