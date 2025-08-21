namespace StarterApp.Api.Infrastructure.Mediator;

public interface IMediator
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
    Task SendAsync(IRequest request, CancellationToken cancellationToken = default);
}

public interface IRequest<out TResponse>
{
}

public interface IRequest
{
}

public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

public interface IRequestHandler<in TRequest> where TRequest : IRequest
{
    Task HandleAsync(TRequest request, CancellationToken cancellationToken);
}



