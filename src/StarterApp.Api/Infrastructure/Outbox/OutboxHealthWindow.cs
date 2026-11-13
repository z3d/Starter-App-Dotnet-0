namespace StarterApp.Api.Infrastructure.Outbox;

internal sealed record OutboxHealthWindow(
    DateTimeOffset StartedOnUtc,
    DateTimeOffset CompletedOnUtc,
    int Published,
    int Errored,
    int Retried,
    int Purged,
    int Paused)
{
    // A window that paused but still published counts as Succeeded. One that paused and published nothing is a stall.
    public string Outcome => Errored > 0 || (Paused > 0 && Published == 0) ? "Degraded" : "Succeeded";

    public string ToSummaryJson() =>
        $"{{\"published\":{Published},\"errored\":{Errored},\"retried\":{Retried},\"purged\":{Purged},\"paused\":{Paused}}}";
}
