using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MovieRental.SharedKernel.Cqrs.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next();
            logger.LogInformation("{Message} handled in {Elapsed} ms", name, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Message} failed after {Elapsed} ms", name, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
