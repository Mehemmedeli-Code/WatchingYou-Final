using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Host.Infrastructure;

/// <summary>
/// Swagger, behind the Admin role and with a way back out.
///
/// Hiding the nav link is presentation, not protection — anyone can type /swagger. The gate
/// below is the actual control, and it reads the session cookie because a browser typing a
/// URL sends no bearer token.
/// </summary>
public static class ApiReference
{
    /// <summary>
    /// What the generated document cannot see on its own: who may call each operation, the
    /// answers the framework gives before any handler runs (401, 403, 429, 400 for a body it
    /// cannot read), and a plain sentence for every answer, so the reference shows each
    /// possible response with what it means and what its body looks like.
    /// </summary>
    public static void Describe(OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "WatchingYou API",
                Version = "v1",
                Description = "Sign in with **POST /api/auth/login**, press **Authorize** and paste the `accessToken`. " +
                              "A padlock marks the calls that need it. Every error comes with a body that says what went wrong: " +
                              "`{ code, message }`, and for invalid fields or parameters also `errors`, the messages by field name."
            };
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Paste the access token returned by /api/auth/login."
            };
            document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
            document.Tags = new HashSet<OpenApiTag>(Tags.Select(t => new OpenApiTag { Name = t.Name, Description = t.Description }));
            return Task.CompletedTask;
        });

        options.AddSchemaTransformer((schema, context, _) =>
        {
            // Numbers are also read from strings ("3"), which the generator spelled out as
            // "integer | string" with a pattern. The API takes and sends numbers; say only that.
            if (schema.Type is { } type && type.HasFlag(JsonSchemaType.String)
                && (type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.Number)))
            {
                schema.Type = type & ~JsonSchemaType.String;
                schema.Pattern = null;
            }
            // A null is never written (fields that are null are left out) and a missing nullable
            // field is read as null, so neither side has to send one: it is not "required".
            if (schema.Required is { Count: > 0 } required && schema.Properties is { } properties)
                foreach (var (name, property) in properties)
                    if (property.Type?.HasFlag(JsonSchemaType.Null) == true
                        || property.OneOf?.Any(s => s.Type == JsonSchemaType.Null) == true)
                        required.Remove(name);
            // The middleware adds the field messages to the same { code, message } body.
            if (context.JsonTypeInfo.Type == typeof(Error))
            {
                schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
                schema.Properties["errors"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Description = "Only when fields or parameters are invalid: their messages, by name (`Email`, `Seats[0].Row`, `Page`).",
                    AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.String } }
                };
            }
            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            var responses = operation.Responses ??= new OpenApiResponses();
            var authorize = metadata.OfType<IAuthorizeData>().ToList();
            var signedIn = authorize.Count > 0 && !metadata.OfType<IAllowAnonymous>().Any();

            if (signedIn)
            {
                responses.TryAdd("401", new OpenApiResponse());
                var staff = authorize.Select(a => a.Policy is { } p && StaffPolicies.TryGetValue(p, out var who) ? who : a.Roles)
                                     .FirstOrDefault(who => !string.IsNullOrEmpty(who));
                if (staff is not null)
                {
                    responses.TryAdd("403", new OpenApiResponse
                    {
                        Description = $"Not allowed: this is for {staff} only, with the staff PIN entered in this browser " +
                                      "(`code`: forbidden, or staff_pin_required when only the PIN is missing)."
                    });
                    operation.Description = $"**Who:** {staff}, with the staff PIN.\n\n{operation.Description}".TrimEnd();
                }
            }
            else
            {
                operation.Security = [];   // no padlock: anyone may call it
            }

            if (metadata.OfType<EnableRateLimitingAttribute>().FirstOrDefault()?.PolicyName is { } limit)
                responses.TryAdd("429", new OpenApiResponse
                {
                    Description = "Too many requests: " + (RateLimits.TryGetValue(limit, out var rule) ? rule : "slow down") +
                                  ". The Retry-After header gives the seconds to wait."
                });

            // 400 before any handler runs: a body that is not JSON, a query value that is not a
            // number or a date, a required one left out. A text parameter cannot fail to read, and
            // a path id with a route constraint ({id:guid}) that does not fit is a 404.
            var parameters = context.Description.ParameterDescriptions;
            if (operation.RequestBody is not null || parameters.Any(p =>
                    (p.Source == BindingSource.Query && (p.IsRequired || p.Type != typeof(string)))
                    || (p.Source == BindingSource.Path && p.Type != typeof(string) && p.RouteInfo?.Constraints?.Any() != true)))
                responses.TryAdd("400", new OpenApiResponse());
            // An id in the path that is not one, or names nothing, is a 404 whatever the handler does.
            if (parameters.Any(p => p.Source == BindingSource.Path)) responses.TryAdd("404", new OpenApiResponse());

            foreach (var (status, item) in responses)
            {
                if (item is not OpenApiResponse response) continue;
                if (string.IsNullOrEmpty(response.Description) || response.Description == ReasonPhrases.GetReasonPhrase(int.Parse(status)))
                    response.Description = Standard.GetValueOrDefault(status) ?? response.Description;
                // Errors the generator saw no body for still have one: { code, message }.
                if (status[0] is '4' && response.Content is not { Count: > 0 })
                    response.Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new() { Schema = new OpenApiSchemaReference(nameof(Error), context.Document) }
                    };
                // Invalid input and clashes found while saving come from the middleware, as a
                // problem document: the same code and message, with title, status and traceId.
                if (status is "400" or "409" && response.Content is { } content && !content.ContainsKey("application/problem+json"))
                    content["application/problem+json"] = new() { Schema = new OpenApiSchemaReference(nameof(Error), context.Document) };
            }

            // In the order of the codes, not the order they were added in.
            var sorted = responses.OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
            responses.Clear();
            foreach (var (status, response) in sorted) responses[status] = response;
            return Task.CompletedTask;
        });
    }

    private static readonly Dictionary<string, string> StaffPolicies = new()
    {
        [AppRoles.SuperAdmin] = "the owner (SuperAdmin)",
        [AppRoles.Admin] = "Admin",
        [AppPolicies.SecurityDesk] = "Security or Admin",
        [AppPolicies.BackOffice] = "Cashier or Admin",
        [AppPolicies.Moderation] = "Moderator, Security or Admin",
    };

    private static readonly Dictionary<string, string> RateLimits = new()
    {
        [AppPolicies.AuthRateLimit] = "more than 8 a minute from one address",
        [AppPolicies.CodeRateLimit] = "more than 12 in 5 minutes from one address",
        [AppPolicies.WriteRateLimit] = "more than 20 a minute from one account",
    };

    private static readonly Dictionary<string, string> Standard = new()
    {
        ["200"] = "OK.",
        ["204"] = "Done. No body.",
        ["400"] = "Bad request: `code` and `message` say what is wrong. Invalid fields or parameters (`code`: validation) " +
                  "also list their messages under `errors`; a body that is not JSON is `code`: invalid_json.",
        ["401"] = "Not signed in: no access token (Authorization: Bearer …) and no session cookie, or the token is " +
                  "invalid or expired (`code`: unauthorized, invalid_token).",
        ["403"] = "Signed in, but not allowed to do this.",
        ["404"] = "Not found: no such item, or it has been deleted.",
        ["409"] = "Refused: it clashes with the item's current state. `message` says why.",
        ["413"] = "The upload is too large.",
        ["429"] = "Too many requests. The Retry-After header gives the seconds to wait.",
    };

    private static readonly (string Name, string Description)[] Tags =
    [
        ("Auth", "Sign-up, sign-in and tokens, e-mail codes, password reset, and the signed-in account."),
        ("Users", "Admin: accounts, roles, suspension, staff PINs and the audit log."),
        ("Globe", "The members' map: cities, who is online where, and comparing film taste."),
        ("Catalog", "Films: browse and search, details, posters, trailers, watching, reviews and recommendations."),
        ("Catalog admin", "Admin: add, edit, delete and restore films, and set their stock."),
        ("Rentals", "Renting, extending and returning films; for staff, the overdue list, figures and exports."),
        ("Watching PRO", "The PRO subscription: status, subscribe and cancel; for staff, the subscribers."),
        ("Cinema", "Screenings, seat maps, booking and paying for seats, tickets, refunds, venues and banners."),
        ("Payments", "Price quotes, card and Stripe checkout, loyalty points; for admins, promo codes."),
        ("Back office", "The cinema till: shifts, box office, bar, ticket types, film deals and reports."),
        ("Shorts", "Short films members upload: upload, stream, visibility and comments; the admin review queue."),
        ("Galleries", "The AI Catalog and Human Craft galleries, and likes."),
        ("Analytics", "Admin dashboards: overview figures and cinema totals."),
        ("System", "Health check."),
        ("Help", "The help chat: members ask, the help desk answers."),
        ("Messages", "Direct messages between members, message requests, blocking and reports."),
        ("People", "Profiles, search, following and follow requests."),
        ("Watchlist", "Each member's list of films to watch later."),
        ("Cinema admin", "Admin: create, edit, cancel and delete screenings."),
        ("Security", "Security desk: the short-film check queue and its reports."),
    ];

    public static void Map(WebApplication app)
    {
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments("/swagger"),
            branch => branch.Use(async (context, next) =>
            {
                // Like any staff screen: nothing here for anyone but an admin...
                if (!context.User.IsInRole(AppRoles.Admin))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                // ...and an admin who has not passed the PIN yet is asked for it, then sent back.
                if (!StaffUnlock.IsUnlocked(context))
                {
                    context.Response.Redirect(StaffPaths.Unlock + "?returnUrl=" + Uri.EscapeDataString("/swagger/index.html"));
                    return;
                }

                await next();
            }));

        // Same URL as before, so it stays behind the Admin gate above.
        app.MapOpenApi("/swagger/{documentName}/swagger.json");
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "WatchingYou API v1");
            options.DocumentTitle = "WatchingYou API";

            // Swagger UI is a third-party bundle; injecting a link is cheaper and safer than
            // forking its assets to add one anchor.
            options.HeadContent = """
                <style>
                  .rr-back {
                    position: fixed; top: 12px; right: 16px; z-index: 9999;
                    display: inline-flex; align-items: center; gap: 6px;
                    padding: 8px 14px; border-radius: 999px;
                    background: #00E676; color: #0A0C0A;
                    font: 600 14px/1 Inter, system-ui, sans-serif;
                    text-decoration: none; box-shadow: 0 2px 14px rgba(0,0,0,.35);
                  }
                  .rr-back:hover { background: #5BFFAB; }
                </style>
                <script>
                  window.addEventListener("DOMContentLoaded", function () {
                    var link = document.createElement("a");
                    link.href = "/";
                    link.className = "rr-back";
                    link.textContent = "\u2190 Back to website";
                    document.body.appendChild(link);
                  });
                </script>
                """;
        });
    }
}
