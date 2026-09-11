namespace StarterApp.Api.Infrastructure.Identity;

// Scoped per request. OwnerAuthorizationBehavior marks the request as one that must authorize
// an owner before it writes; OwnerOnlyPolicy.Authorize marks that it did. OwnerAuthorizationWriteGuard
// reads both before any write reaches the database.
public sealed class OwnerPolicyEvaluationTracker
{
    public bool RequiresEvaluation { get; private set; }

    public bool WasEvaluated { get; private set; }

    public void RequireEvaluation() => RequiresEvaluation = true;

    public void MarkEvaluated() => WasEvaluated = true;

    public bool IsViolated => RequiresEvaluation && !WasEvaluated;
}
