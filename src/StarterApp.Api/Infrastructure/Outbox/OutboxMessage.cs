using System.Text.Json;
using System.Text.Json.Serialization;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Api.Infrastructure.Outbox;

public class OutboxMessage
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Guid Id { get; private set; }
    public DateTimeOffset OccurredOnUtc { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset? ProcessedOnUtc { get; private set; }
    public int RetryCount { get; private set; }
    public string? Error { get; private set; }
    public Guid? ProcessingId { get; private set; }
    public DateTimeOffset? LockedUntilUtc { get; private set; }
    public int ReplayCount { get; private set; }
    public DateTimeOffset? ReplayedOnUtc { get; private set; }

    private OutboxMessage()
    {
    }

    public void MarkAsProcessed(DateTimeOffset processedOnUtc)
    {
        ProcessedOnUtc = processedOnUtc;
        ClearClaim();
    }

    public void IncrementRetry()
    {
        RetryCount++;
        ClearClaim();
    }

    public void MarkAsError(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        Error = error;
        ClearClaim();
    }

    public void ResetForReplay(DateTimeOffset replayedOnUtc)
    {
        // The sanctioned recovery path for errored rows (DbMigrator replay-outbox
        // verb mirrors this in SQL; OutboxReplayTests keeps both in sync).
        if (ProcessedOnUtc is not null)
            throw new InvalidOperationException("A processed outbox message cannot be replayed.");

        if (Error is null)
            throw new InvalidOperationException("Only errored outbox messages can be replayed.");

        Error = null;
        RetryCount = 0;
        ReplayCount++;
        ReplayedOnUtc = replayedOnUtc;
        ClearClaim();
    }

    public void Claim(Guid processingId, DateTimeOffset lockedUntilUtc)
    {
        if (processingId == Guid.Empty)
            throw new ArgumentException("Processing id cannot be empty", nameof(processingId));

        if (lockedUntilUtc == default)
            throw new ArgumentOutOfRangeException(nameof(lockedUntilUtc), "Lock expiry must be specified");

        ProcessingId = processingId;
        LockedUntilUtc = lockedUntilUtc;
    }

    private void ClearClaim()
    {
        ProcessingId = null;
        LockedUntilUtc = null;
    }

    public static OutboxMessage Create(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            Type = domainEvent.EventType,
            CorrelationId = CorrelationContext.GetOrCreate(),
            Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), SerializerOptions)
        };
    }
}
