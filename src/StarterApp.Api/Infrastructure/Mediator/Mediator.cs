using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Api.Infrastructure.Mediator;

public class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;

    public Mediator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        RunValidators(request);

        var requestType = request.GetType();
        var responseType = typeof(TResponse);
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, responseType);

        var handler = _serviceProvider.GetService(handlerType);
        if (handler == null)
        {
            throw new InvalidOperationException($"No handler registered for {requestType.Name}");
        }

        var method = handlerType.GetMethod(nameof(IRequestHandler<IRequest<TResponse>, TResponse>.HandleAsync));
        if (method == null)
        {
            throw new InvalidOperationException($"HandleAsync method not found on {handlerType.Name}");
        }

        var result = await (Task<TResponse>)method.Invoke(handler, new object[] { request, cancellationToken })!;
        return result;
    }

    public async Task SendAsync(IRequest request, CancellationToken cancellationToken = default)
    {
        RunValidators(request);

        var requestType = request.GetType();
        var handlerType = typeof(IRequestHandler<>).MakeGenericType(requestType);

        var handler = _serviceProvider.GetService(handlerType);
        if (handler == null)
        {
            throw new InvalidOperationException($"No handler registered for {requestType.Name}");
        }

        var method = handlerType.GetMethod(nameof(IRequestHandler<IRequest>.HandleAsync));
        if (method == null)
        {
            throw new InvalidOperationException($"HandleAsync method not found on {handlerType.Name}");
        }

        await (Task)method.Invoke(handler, new object[] { request, cancellationToken })!;
    }

    private void RunValidators<T>(T request)
    {
        var validatorType = typeof(IValidator<>).MakeGenericType(request!.GetType());
        var validators = _serviceProvider.GetServices(validatorType);

        var errors = new List<ValidationError>();
        foreach (var validator in validators)
        {
            var validateMethod = validatorType.GetMethod(nameof(IValidator<object>.Validate));
            if (validateMethod == null)
                continue;

            var result = validateMethod.Invoke(validator, new object[] { request });
            if (result is IEnumerable<ValidationError> validationErrors)
            {
                errors.AddRange(validationErrors);
            }
        }

        if (errors.Count > 0)
        {
            throw new Validation.ValidationException(errors);
        }
    }
}
