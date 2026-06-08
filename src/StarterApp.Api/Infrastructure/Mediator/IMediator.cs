namespace StarterApp.Api.Infrastructure.Mediator;

public interface IMediator
{
    // Single dispatch path: every request is IRequest<TResponse>, so every command/query runs
    // through the same IPipelineBehavior chain. Commands with no natural result return Unit.
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

public interface IRequest<out TResponse>
{
}

public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}



