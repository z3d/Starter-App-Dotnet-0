namespace StarterApp.Api.Infrastructure.Identity;

public sealed class OwnerAuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly OwnerPolicyEvaluationTracker _tracker;
    private readonly IHostEnvironment _environment;

    public OwnerAuthorizationBehavior(OwnerPolicyEvaluationTracker tracker, IHostEnvironment environment)
    {
        _tracker = tracker;
        _environment = environment;
    }

    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();

        // Only asserted on non-exceptional completion: a handler that threw
        // (not found, forbidden, validation) failed the request on its own.
        if (request is IOwnerAuthorizedMutation && !_tracker.WasEvaluated)
        {
            var message = $"{request.GetType().Name} completed without consulting IOwnerOnlyPolicy.Authorize. " +
                          "Every IOwnerAuthorizedMutation handler must authorize the loaded aggregate's owner before mutating it.";

            // In production the mutation is already persisted by the time this runs,
            // so failing the request cannot undo it — log loudly instead of turning
            // a detector into an outage. Development/Testing fail hard so the gap
            // can never get past the test suite.
            if (_environment.IsDevelopment() || _environment.IsEnvironment("Testing"))
                throw new InvalidOperationException(message);

            Log.Error("Owner-policy enforcement violation: {ViolationMessage}", message);
        }

        return response;
    }
}
