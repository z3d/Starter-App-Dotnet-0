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
        // Flagged before the handler so OwnerAuthorizationWriteGuard can refuse a write with no owner check.
        if (request is IOwnerAuthorizedMutation)
            _tracker.RequireEvaluation();

        var response = await next();

        // A handler that completed without writing and without authorizing is still a bug.
        if (_tracker.IsViolated)
        {
            throw new InvalidOperationException(
                $"{request.GetType().Name} completed without consulting IOwnerOnlyPolicy.Authorize. " +
                "Every IOwnerAuthorizedMutation handler must authorize the loaded aggregate's owner before mutating it.");
        }

        return response;
    }
}
