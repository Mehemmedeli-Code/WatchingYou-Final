using FluentValidation;

namespace MovieRental.SharedKernel.Cqrs.Behaviors;

/// <summary>Runs every FluentValidation validator registered for the message before the
/// handler sees it, so handlers can assume well-formed input.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken ct)
    {
        var active = validators.ToArray();
        if (active.Length == 0) return await next();

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(active.Select(v => v.ValidateAsync(context, ct)));
        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToArray();

        if (failures.Length > 0) throw new ValidationException(failures);
        return await next();
    }
}
