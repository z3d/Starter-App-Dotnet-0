namespace StarterApp.ServiceDefaults.Jobs;

public sealed record LastRun(DateTimeOffset StartedOnUtc, string? CompletedOutcome, DateTimeOffset? UnfinishedSinceUtc = null);
