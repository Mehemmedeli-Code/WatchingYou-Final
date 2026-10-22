using System.Text.Json.Serialization;
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
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AppRoles.Admin, policy => policy.RequireRole(AppRoles.Admin))
    .AddPolicy(AppPolicies.SecurityDesk, policy => policy.RequireRole(AppRoles.Security, AppRoles.Admin))
    .AddPolicy(AppRoles.Customer, policy => policy.RequireAuthenticatedUser());

// ---------------------------------------------------------------------------
// Presentation
// ---------------------------------------------------------------------------
builder.Services.AddRazorPages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Reel & Row API", Version = "v1" });

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

// The Vite dev server runs on its own origin while you develop; in production the React
// bundle is served by this host, so no cross-origin request exists at all.
const string DevCors = "vite-dev";
builder.Services.AddCors(options => options.AddPolicy(DevCors, policy => policy
    .WithOrigins(builder.Configuration["Frontend:DevServerUrl"] ?? "http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// ---------------------------------------------------------------------------
// Pipeline. Order matters: exceptions first so everything below is covered, and
// authentication before the Swagger gate so it has an identity to check.
// ---------------------------------------------------------------------------
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseCors(DevCors);
    await DevelopmentDatabaseBootstrapper.InitialiseAsync(app.Services);
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

ApiReference.Map(app);

app.MapRazorPages();
app.MapModules();
AnalyticsEndpoints.Map(app);
LanguageEndpoints.Map(app);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }))
   .WithTags("System").AllowAnonymous();

app.Run();
