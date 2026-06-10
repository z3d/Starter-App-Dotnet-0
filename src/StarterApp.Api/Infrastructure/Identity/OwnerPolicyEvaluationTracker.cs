namespace StarterApp.Api.Infrastructure.Identity;

public sealed class OwnerPolicyEvaluationTracker
{
    public bool WasEvaluated { get; private set; }

    public void MarkEvaluated() => WasEvaluated = true;
}
