using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace MovieRental.SharedKernel.Cqrs;

/// <summary>
/// Minimal in-process mediator. A third-party mediator would add a dependency for
/// roughly this much code, and hand-rolling it keeps the pipeline visible to readers.
/// Executors are resolved by reflection once per message type and then cached.
/// </summary>
public sealed class Dispatcher(IServiceProvider provider) : IDispatcher
{
    private static readonly ConcurrentDictionary<(Type Message, Type Response), object> Executors = new();

    public Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var executor = (CommandExecutor<TResponse>)Executors.GetOrAdd(
            (command.GetType(), typeof(TResponse)),
            static key => Activator.CreateInstance(
                typeof(CommandExecutor<,>).MakeGenericType(key.Message, key.Response))!);
        return executor.Execute(command, provider, ct);
    }

    public Task<TResponse> Ask<TResponse>(IQuery<TResponse> query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var executor = (QueryExecutor<TResponse>)Executors.GetOrAdd(
            (query.GetType(), typeof(TResponse)),
            static key => Activator.CreateInstance(
                typeof(QueryExecutor<,>).MakeGenericType(key.Message, key.Response))!);
        return executor.Execute(query, provider, ct);
    }

    private static Task<TResponse> RunPipeline<TRequest, TResponse>(
        TRequest request, Func<Task<TResponse>> handler, IServiceProvider provider, CancellationToken ct)
    {
        var behaviors = provider.GetServices<IPipelineBehavior<TRequest, TResponse>>().ToArray();
        var next = handler;
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var inner = next;
            next = () => behavior.Handle(request, inner, ct);
        }
        return next();
    }

    private abstract class CommandExecutor<TResponse>
    {
        public abstract Task<TResponse> Execute(object command, IServiceProvider provider, CancellationToken ct);
    }

    private sealed class CommandExecutor<TCommand, TResponse> : CommandExecutor<TResponse>
        where TCommand : ICommand<TResponse>
    {
        public override Task<TResponse> Execute(object command, IServiceProvider provider, CancellationToken ct)
        {
            var typed = (TCommand)command;
            var handler = provider.GetRequiredService<ICommandHandler<TCommand, TResponse>>();
            return RunPipeline<TCommand, TResponse>(typed, () => handler.Handle(typed, ct), provider, ct);
        }
    }

    private abstract class QueryExecutor<TResponse>
    {
        public abstract Task<TResponse> Execute(object query, IServiceProvider provider, CancellationToken ct);
    }

    private sealed class QueryExecutor<TQuery, TResponse> : QueryExecutor<TResponse>
        where TQuery : IQuery<TResponse>
    {
        public override Task<TResponse> Execute(object query, IServiceProvider provider, CancellationToken ct)
        {
            var typed = (TQuery)query;
            var handler = provider.GetRequiredService<IQueryHandler<TQuery, TResponse>>();
            return RunPipeline<TQuery, TResponse>(typed, () => handler.Handle(typed, ct), provider, ct);
        }
    }
}
