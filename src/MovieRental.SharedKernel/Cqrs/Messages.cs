namespace MovieRental.SharedKernel.Cqrs;

/// <summary>Write-side message. Handled by exactly one handler, runs inside a transaction.</summary>
public interface ICommand<TResponse>;

/// <summary>Read-side message. Handled by exactly one handler, never mutates state.</summary>
public interface IQuery<TResponse>;

public interface ICommandHandler<TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    Task<TResponse> Handle(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<TQuery, TResponse> where TQuery : IQuery<TResponse>
{
    Task<TResponse> Handle(TQuery query, CancellationToken ct);
}

/// <summary>Cross-cutting step wrapped around every handler. Kept deliberately small:
/// validation and logging only. Anything module-specific belongs in the slice itself.</summary>
public interface IPipelineBehavior<TRequest, TResponse>
{
    Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken ct);
}

public interface IDispatcher
{
    Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default);
    Task<TResponse> Ask<TResponse>(IQuery<TResponse> query, CancellationToken ct = default);
}
