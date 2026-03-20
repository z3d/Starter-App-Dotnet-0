using StarterApp.Domain.Events;
using System.Text.Json;

namespace StarterApp.Api.Infrastructure.Outbox;

public class OutboxMessage
{
    public Guid Id { get; private set; }
    public DateTime OccurredOnUtc { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTime? ProcessedOnUtc { get; private set; }
    public string? Error { get; private set; }

    private OutboxMessage()
    {
    }

    public static OutboxMessage Create(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        (string type, object payload) = domainEvent switch
        {
            OrderCreatedDomainEvent created => (
                nameof(OrderCreatedDomainEvent),
                (object)new OrderCreatedOutboxPayload(
                    created.Order.Id,
                    created.Order.CustomerId,
                    created.Order.Status.ToString(),
                    created.LineItemCount,
                    created.TotalQuantity,
                    created.TotalExcludingGst,
                    created.TotalIncludingGst,
                    created.TotalGstAmount,
                    created.Currency)),
            OrderStatusChangedDomainEvent statusChanged => (
                nameof(OrderStatusChangedDomainEvent),
                (object)new OrderStatusChangedOutboxPayload(
                    statusChanged.Order.Id,
                    statusChanged.Order.CustomerId,
                    statusChanged.PreviousStatus,
                    statusChanged.NewStatus,
                    statusChanged.Order.LastUpdated)),
            _ => throw new NotSupportedException($"Unsupported domain event type '{domainEvent.GetType().Name}' for outbox persistence")
        };

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = domainEvent.OccurredOnUtc,
            Type = type,
            Payload = JsonSerializer.Serialize(payload)
        };
    }

    private sealed record OrderCreatedOutboxPayload(
        int OrderId,
        int CustomerId,
        string Status,
        int LineItemCount,
        int TotalQuantity,
        decimal TotalExcludingGst,
        decimal TotalIncludingGst,
        decimal TotalGstAmount,
        string Currency);

    private sealed record OrderStatusChangedOutboxPayload(
        int OrderId,
        int CustomerId,
        string PreviousStatus,
        string NewStatus,
        DateTime LastUpdated);
}
