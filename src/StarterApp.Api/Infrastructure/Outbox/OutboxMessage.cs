using System.Text.Json;
using System.Text.Json.Serialization;

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
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset? ProcessedOnUtc { get; private set; }
    public int RetryCount { get; private set; }
    public string? Error { get; private set; }

    private OutboxMessage()
    {
    }

    public void MarkAsProcessed(DateTimeOffset processedOnUtc)
    {
        ProcessedOnUtc = processedOnUtc;
    }

    public void IncrementRetry()
    {
        RetryCount++;
    }

    public void MarkAsError(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        Error = error;
    }

    public static OutboxMessage Create(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            Type = domainEvent.GetType().Name,
            Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), SerializerOptions)
        };
    }
}
