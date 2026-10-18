namespace StarterApp.Api.Infrastructure.Identity;

// One per request. The behaviour marks that an owner check is required, the policy marks that it ran, and the write guard reads both.
public sealed class OwnerPolicyEvaluationTracker
{
    public bool RequiresEvaluation { get; private set; }

    public bool WasEvaluated { get; private set; }

    public void RequireEvaluation() => RequiresEvaluation = true;

    public void MarkEvaluated() => WasEvaluated = true;

    public bool IsViolated => RequiresEvaluation && !WasEvaluated;
}
