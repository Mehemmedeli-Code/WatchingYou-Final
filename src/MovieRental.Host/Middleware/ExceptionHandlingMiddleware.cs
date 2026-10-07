using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace MovieRental.Host.Middleware;

/// <summary>
/// Single place where an exception becomes an HTTP response.
///
/// API calls get RFC 7807 ProblemDetails, so the client has one error shape to handle. A
/// browser navigating to a page gets the error page instead — a visitor who mistyped a URL
/// should not be shown a wall of JSON.
/// </summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                logger.LogError(ex, "Response already started; the exception cannot be converted to a problem.");
                throw;
            }

            var (status, title, errors) = Describe(ex);
            if (status >= StatusCodes.Status500InternalServerError)
                logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
            else
                logger.LogWarning("{Title} on {Method} {Path}", title, context.Request.Method, context.Request.Path);

            context.Response.Clear();
            context.Response.StatusCode = status;

            // A page request is anything that is not under /api and would accept HTML.
            if (!context.Request.Path.StartsWithSegments("/api") &&
                context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect($"/error/{status}");
                return;
            }

            context.Response.ContentType = "application/problem+json";

            var payload = new
            {
                type = $"https://httpstatuses.io/{status}",
                title,
                status,
                traceId = context.TraceIdentifier,
                errors
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
    }

    /// <summary>Nginx's convention for "the caller hung up"; ASP.NET Core has no constant for it.</summary>
    private const int ClientClosedRequest = 499;

    private static (int Status, string Title, IDictionary<string, string[]>? Errors) Describe(Exception ex) => ex switch
    {
        ValidationException validation => (
            StatusCodes.Status400BadRequest,
            "One or more fields need fixing.",
            validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),

        // Thrown by ASP.NET itself before any handler runs: a body that is not valid JSON, a
        // query value that cannot be read as the type the endpoint declares (?from=yesterday
        // for a date), a missing required parameter. It carries the right status, usually 400
        // — falling through to the catch-all below made every one of those a 500.
        BadHttpRequestException bad => (bad.StatusCode, "The request could not be read. Check the body and the parameters.", null),
        JsonException => (StatusCodes.Status400BadRequest, "The body is not valid JSON.", null),

        UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Sign in to continue.", null),
        KeyNotFoundException => (StatusCodes.Status404NotFound, "That item no longer exists.", null),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Someone else changed this first. Reload and retry.", null),
        DbUpdateException => (StatusCodes.Status409Conflict, "That change conflicts with existing data.", null),
        OperationCanceledException => (ClientClosedRequest, "The request was cancelled.", null),
        _ => (StatusCodes.Status500InternalServerError, "Something went wrong on our side.", null)
    };
}
