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
