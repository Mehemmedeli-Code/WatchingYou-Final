using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Metadata;

namespace MovieRental.Host.Infrastructure;

/// <summary>
/// Every error the API sends says what went wrong, as { code, message }. A 401, 403, 404, 405
/// or 415 used to come back with an empty body: the API reference showed a bare number, and a
/// caller had nothing to show its user but "Request failed (401)".
/// </summary>
internal static class ApiErrors
{
    public static bool IsApi(HttpContext context) => context.Request.Path.StartsWithSegments("/api");

    public static Task WriteAsync(HttpContext context, string code, string message) =>
        context.Response.WriteAsJsonAsync(new { code, message });

    /// <summary>For an error response that a handler or the framework left without a body.</summary>
    public static Task DescribeStatus(StatusCodeContext status)
    {
        var http = status.HttpContext;
        var (code, message) = http.Response.StatusCode switch
        {
            400 => ("bad_request", "The request could not be read. Check the body and the parameters."),
            401 when http.Request.Headers.Authorization.Count > 0 =>
                ("invalid_token", "The access token is invalid or has expired. Sign in again, or get a new one from /api/auth/refresh."),
            401 => ("unauthorized", "Sign in first: send the access token from /api/auth/login as \"Authorization: Bearer <token>\"."),
            403 => ("forbidden", "This account is not allowed to do this."),
            404 => ("not_found", "Nothing was found here: the address is wrong, or the item does not exist."),
            405 => ("method_not_allowed", $"This address does not take {http.Request.Method}. See the API reference for the methods it takes."),
            409 => ("conflict", "This clashes with the current state of the item. Reload and try again."),
            413 => ("payload_too_large", "The upload is too large."),
            415 => ("unsupported_media_type", "Send the body as " + Accepted(http) + "."),
            _ => ("error", "The request failed.")
        };
        return WriteAsync(http, code, message);
    }

    private static string Accepted(HttpContext http) =>
        http.GetEndpoint()?.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes is { Count: > 0 } types
            ? string.Join(" or ", types)
            : "application/json";
}

/// <summary>
/// Tells a member of staff who has the role but has not entered the staff PIN that the PIN is
/// what is missing; to everyone else a refusal stays a plain "not allowed".
/// </summary>
internal sealed class ApiAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler standard = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        await standard.HandleAsync(next, context, policy, result);
        if (result.Forbidden && ApiErrors.IsApi(context) && !context.Response.HasStarted
            && result.AuthorizationFailure?.FailedRequirements.All(r => r is StaffUnlockedRequirement) == true)
            await ApiErrors.WriteAsync(context, "staff_pin_required",
                "Enter your staff PIN first: open the staff unlock page in this browser, then try again.");
    }
}
