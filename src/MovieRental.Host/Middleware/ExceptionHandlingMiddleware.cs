using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MovieRental.Host.Middleware;

/// <summary>
/// Single place where an exception becomes an HTTP response.
///
/// API calls get RFC 7807 ProblemDetails carrying the same `code` and `message` as every other
/// error the API sends, so the client has one error shape to handle. A browser navigating to a
/// page gets the error page instead — a visitor who mistyped a URL should not be shown a wall
/// of JSON.
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

            var (status, code, title, errors) = Describe(ex);
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
                code,
                message = title,
                errors,
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload, Json));
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Nginx's convention for "the caller hung up"; ASP.NET Core has no constant for it.</summary>
    private const int ClientClosedRequest = 499;

    /// <summary>"$.seats[0].row" → "Seats[0].Row", the way the validation errors name fields.</summary>
    private static string FieldName(string jsonPath) =>
        string.Join('.', jsonPath.TrimStart('$', '.').Split('.').Select(part => part.Length > 0 ? char.ToUpperInvariant(part[0]) + part[1..] : part));

    /// <summary>
    /// The parameter ASP.NET could not read, from its message: <c>Failed to bind parameter "int? Page"
    /// from "x".</c> or <c>Required parameter "string city" was not provided from query string.</c>
    /// A body parameter is left out: its name ("command") would tell the caller nothing.
    /// </summary>
    private static readonly Regex BindFailure = new(
        "^(?<missing>Required )?(?:Failed to bind )?parameter \"(?:[^\"]* )?(?<name>\\w+)\" (?:from \"|was not provided from (?!body))",
        RegexOptions.CultureInvariant);

    private static Dictionary<string, string[]>? ParameterErrors(BadHttpRequestException bad) =>
        BindFailure.Match(bad.Message) is { Success: true } match
            ? new Dictionary<string, string[]>
            {
                [FieldName(match.Groups["name"].Value)] =
                    [match.Groups["missing"].Success ? "This value is required." : "This value has the wrong type or format."]
            }
            : null;

    /// <summary>A number too big for its column, or text too long for it: the input's fault, not a clash.</summary>
    private static bool TooBigToStore(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 8115 or 8152 or 2628 or 220 or 232 or 242 or 517 };

    private static (int Status, string Code, string Title, IDictionary<string, string[]>? Errors) Describe(Exception ex) => ex switch
    {
        ValidationException validation => (
            StatusCodes.Status400BadRequest, "validation",
            "One or more fields need fixing.",
            validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),

        // Thrown by ASP.NET itself before any handler runs: a body that is not valid JSON, a
        // query value that cannot be read as the type the endpoint declares (?from=yesterday
        // for a date), a missing required parameter. It carries the right status, usually 400
        // — falling through to the catch-all below made every one of those a 500.
        BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } =>
            (StatusCodes.Status413PayloadTooLarge, "payload_too_large", "The upload is too large.", null),
        // Valid JSON with a field of the wrong type ("email": 5, "seats": "A1"): name the field.
        BadHttpRequestException { InnerException: JsonException { Path: { Length: > 1 } path } } => (
            StatusCodes.Status400BadRequest, "validation",
            "One or more fields need fixing.",
            new Dictionary<string, string[]> { [FieldName(path)] = ["This field has the wrong type or format."] }),
        BadHttpRequestException { InnerException: JsonException } =>
            (StatusCodes.Status400BadRequest, "invalid_json", "The body is not valid JSON.", null),
        // A query, route or header value that is not what the call takes (?page=abc): name it too.
        BadHttpRequestException bad when ParameterErrors(bad) is { } errors =>
            (bad.StatusCode, "validation", "One or more fields need fixing.", errors),
        BadHttpRequestException bad when bad.Message.Contains("body", StringComparison.OrdinalIgnoreCase) =>
            (bad.StatusCode, "bad_request", "The body is missing. Send the fields as JSON.", null),
        BadHttpRequestException bad =>
            (bad.StatusCode, "bad_request", "The request could not be read. Check the body and the parameters.", null),
        JsonException => (StatusCodes.Status400BadRequest, "invalid_json", "The body is not valid JSON.", null),

        UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "unauthorized", "Sign in to continue.", null),
        KeyNotFoundException => (StatusCodes.Status404NotFound, "not_found", "That item no longer exists.", null),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "conflict", "Someone else changed this first. Reload and retry.", null),
        DbUpdateException tooBig when TooBigToStore(tooBig) =>
            (StatusCodes.Status400BadRequest, "validation", "A value is too large or too long to store.", null),
        DbUpdateException => (StatusCodes.Status409Conflict, "conflict", "That change conflicts with existing data.", null),
        OperationCanceledException => (ClientClosedRequest, "cancelled", "The request was cancelled.", null),
        _ => (StatusCodes.Status500InternalServerError, "server_error", "Something went wrong on our side.", null)
    };
}
