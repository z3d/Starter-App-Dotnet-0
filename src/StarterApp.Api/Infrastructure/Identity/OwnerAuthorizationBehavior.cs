namespace StarterApp.Api.Infrastructure.Identity;

public sealed class OwnerAuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly OwnerPolicyEvaluationTracker _tracker;

    public OwnerAuthorizationBehavior(OwnerPolicyEvaluationTracker tracker)
    {
        _tracker = tracker;
    }

    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // Flag the request before the handler runs. OwnerAuthorizationWriteGuard then refuses
        // any write from a handler that has not called IOwnerOnlyPolicy.Authorize, so a missing
        // check fails the request before anything is persisted, in every environment.
        if (request is IOwnerAuthorizedMutation)
            _tracker.RequireEvaluation();

        var response = await next();

        // A handler that threw already failed the request on its own. One that completed without
        // writing and without authorizing is still a bug, so fail it here.
        if (_tracker.IsViolated)
        {
            throw new InvalidOperationException(
                $"{request.GetType().Name} completed without consulting IOwnerOnlyPolicy.Authorize. " +
                "Every IOwnerAuthorizedMutation handler must authorize the loaded aggregate's owner before mutating it.");
        }

        return response;
    }
}
