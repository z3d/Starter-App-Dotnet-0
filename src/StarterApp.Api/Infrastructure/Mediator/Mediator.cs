using System.Collections.Concurrent;

namespace StarterApp.Api.Infrastructure.Mediator;

public class Mediator : IMediator
{
    // One wrapper is built per concrete request type on first dispatch and cached
    // for the process lifetime. MakeGenericType / Activator.CreateInstance runs
    // once per request type — every subsequent SendAsync is a dictionary lookup
    // plus a strongly-typed virtual call. No MethodInfo.Invoke, no per-call
    // object[] argument allocation, no per-behavior GetMethod lookup.
    private static readonly ConcurrentDictionary<Type, object> RequestHandlerWrappers = new();
    private static readonly ConcurrentDictionary<Type, VoidRequestHandlerWrapper> VoidRequestHandlerWrappers = new();
    private static readonly ConcurrentDictionary<Type, ValidatorInvoker> ValidatorInvokers = new();

    private readonly IServiceProvider _serviceProvider;

    public Mediator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RunValidators(request);

        var wrapper = (RequestHandlerWrapper<TResponse>)RequestHandlerWrappers.GetOrAdd(
            request.GetType(),
            static (requestType, responseType) =>
                Activator.CreateInstance(typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(requestType, responseType))!,
            typeof(TResponse));

        return wrapper.HandleAsync(request, _serviceProvider, cancellationToken);
    }

    public Task SendAsync(IRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RunValidators(request);

        var wrapper = VoidRequestHandlerWrappers.GetOrAdd(
            request.GetType(),
            static requestType =>
                (VoidRequestHandlerWrapper)Activator.CreateInstance(typeof(VoidRequestHandlerWrapperImpl<>).MakeGenericType(requestType))!);

        return wrapper.HandleAsync(request, _serviceProvider, cancellationToken);
    }

    private void RunValidators(object request)
    {
        var invoker = ValidatorInvokers.GetOrAdd(
            request.GetType(),
            static requestType =>
                (ValidatorInvoker)Activator.CreateInstance(typeof(ValidatorInvokerImpl<>).MakeGenericType(requestType))!);

        var errors = invoker.Validate(request, _serviceProvider);
        if (errors.Count > 0)
            throw new Validation.ValidationException(errors);
    }

    private abstract class RequestHandlerWrapper<TResponse>
    {
        public abstract Task<TResponse> HandleAsync(IRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken);
    }

    private sealed class RequestHandlerWrapperImpl<TRequest, TResponse> : RequestHandlerWrapper<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> HandleAsync(IRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
                ?? throw new InvalidOperationException($"No handler registered for {typeof(TRequest).Name}");

            var typed = (TRequest)request;
            RequestHandlerDelegate<TResponse> pipeline = () => handler.HandleAsync(typed, cancellationToken);

            var behaviors = serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
            foreach (var behavior in behaviors.Reverse())
            {
                var next = pipeline;
                pipeline = () => behavior.HandleAsync(typed, next, cancellationToken);
            }

            return pipeline();
        }
    }

    private abstract class VoidRequestHandlerWrapper
    {
        public abstract Task HandleAsync(IRequest request, IServiceProvider serviceProvider, CancellationToken cancellationToken);
    }

    private sealed class VoidRequestHandlerWrapperImpl<TRequest> : VoidRequestHandlerWrapper
        where TRequest : IRequest
    {
        public override Task HandleAsync(IRequest request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var handler = serviceProvider.GetService<IRequestHandler<TRequest>>()
                ?? throw new InvalidOperationException($"No handler registered for {typeof(TRequest).Name}");
            return handler.HandleAsync((TRequest)request, cancellationToken);
        }
    }

    private abstract class ValidatorInvoker
    {
        public abstract IReadOnlyList<ValidationError> Validate(object request, IServiceProvider serviceProvider);
    }

    private sealed class ValidatorInvokerImpl<TRequest> : ValidatorInvoker
    {
        public override IReadOnlyList<ValidationError> Validate(object request, IServiceProvider serviceProvider)
        {
            var validators = serviceProvider.GetServices<IValidator<TRequest>>();
            List<ValidationError>? errors = null;
            var typed = (TRequest)request;
            foreach (var validator in validators)
            {
                foreach (var error in validator.Validate(typed))
                {
                    (errors ??= []).Add(error);
                }
            }
            return (IReadOnlyList<ValidationError>?)errors ?? Array.Empty<ValidationError>();
        }
    }
}
