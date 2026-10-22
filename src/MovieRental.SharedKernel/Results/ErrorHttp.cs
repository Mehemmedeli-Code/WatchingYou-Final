using Microsoft.AspNetCore.Http;

namespace MovieRental.SharedKernel.Results;

/// <summary>The HTTP answer for a failed result, the same mapping every endpoint uses.</summary>
public static class ErrorHttp
{
    public static IResult ToHttp(this Error error) => error.Code switch
    {
        "not_found" => Microsoft.AspNetCore.Http.Results.NotFound(error),
        "conflict" => Microsoft.AspNetCore.Http.Results.Conflict(error),
        "forbidden" => Microsoft.AspNetCore.Http.Results.Json(error, statusCode: StatusCodes.Status403Forbidden),
        "unauthorized" => Microsoft.AspNetCore.Http.Results.Json(error, statusCode: StatusCodes.Status401Unauthorized),
        _ => Microsoft.AspNetCore.Http.Results.BadRequest(error),
    };

    /// <summary>200 with the value, or the error's status.</summary>
    public static IResult ToHttp<T>(this Result<T> result) => result.IsSuccess ? Microsoft.AspNetCore.Http.Results.Ok(result.Value) : result.Error.ToHttp();
}
