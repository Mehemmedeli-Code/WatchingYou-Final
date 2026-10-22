namespace MovieRental.SharedKernel.Results;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error NotFound(string what) => new("not_found", $"{what} was not found.");
    public static Error Conflict(string message) => new("conflict", message);
    public static Error Validation(string message) => new("validation", message);
    public static Error Forbidden(string message) => new("forbidden", message);
    public static Error Unauthorized(string message) => new("unauthorized", message);
}
