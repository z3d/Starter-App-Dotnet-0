namespace StarterApp.Api.Infrastructure.Identity;

// Scoped per request: the behaviour marks that authorization is required, the policy marks that it ran, the guard reads both.
public sealed class OwnerPolicyEvaluationTracker
{
    public bool RequiresEvaluation { get; private set; }

    public bool WasEvaluated { get; private set; }

    public void RequireEvaluation() => RequiresEvaluation = true;

    public void MarkEvaluated() => WasEvaluated = true;

    public bool IsViolated => RequiresEvaluation && !WasEvaluated;
}
