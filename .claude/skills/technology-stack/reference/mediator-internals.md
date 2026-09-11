# Custom Mediator Internals

`src/StarterApp.Api/Infrastructure/Mediator/`. Replaces MediatR — commercial licensing was the trigger, but owning it means the entire dispatch path is one readable file with no per-call reflection.

## The contract

```csharp
public interface IMediator
{
    // Single dispatch path: every request is IRequest<TResponse>, so every command/query runs
    // through the same IPipelineBehavior chain. Commands with no natural result return Unit.
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

public interface IRequest<out TResponse> { }

public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}
```

## Dispatch

The implementation deliberately avoids `MethodInfo.Invoke`. It runs the feature-toggle gate and validators, then dispatches through a strongly-typed wrapper **built once per request type and cached for the process lifetime**:

```csharp
public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(request);

    RunFeatureToggleGate(request);  // [FeatureToggle("name")] -> FeatureDisabledException (503) before anything else
    RunValidators(request);         // every command/query has a validator (convention-enforced)

    var wrapper = (RequestHandlerWrapper<TResponse>)RequestHandlerWrappers.GetOrAdd(
        request.GetType(),
        static (requestType, responseType) =>
            Activator.CreateInstance(typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(requestType, responseType))!,
        typeof(TResponse));

    return wrapper.HandleAsync(request, _serviceProvider, cancellationToken);
}
```

Inside `RequestHandlerWrapperImpl<TRequest, TResponse>`: resolve `IRequestHandler<TRequest, TResponse>` from the service provider, wrap the call in the `IPipelineBehavior<TRequest, TResponse>` chain (registration order — first registered runs outermost), and invoke `handler.HandleAsync(typed, cancellationToken)` directly.

So the cost profile is: **one** reflective `MakeGenericType` + `Activator.CreateInstance` per request *type* per process, then a `ConcurrentDictionary` lookup plus a typed virtual call per request — no `object[]` argument allocation, no `MethodInfo.Invoke` on the hot path.

## Why the ordering matters

1. **Feature-toggle gate first** — before validators and before `CachingBehavior`. If the toggle check ran inside the pipeline, a disabled `ICacheable` query could still be served from cache, which turns a kill switch into a partial kill switch.
2. **Validators before behaviors** — behaviors can assume the request is shape-valid.
3. **Behaviors in registration order** — first registered is outermost. `CachingBehavior` wraps `ICacheable` queries; `OwnerAuthorizationBehavior` flags `IOwnerAuthorizedMutation` commands before the handler runs so `OwnerAuthorizationWriteGuard` can refuse an unauthorized write, and asserts afterwards that the owner policy ran.

Everything a request passes through is centralized on this one path — there is deliberately no second dispatch mechanism, no notification fan-out, and no handler-to-handler dispatch (`IMediator` injection into handlers is convention-banned in the repos that carry that rule).
