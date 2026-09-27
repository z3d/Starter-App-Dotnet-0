namespace StarterApp.Api.Infrastructure.Idempotency;

// One row per keyed create, scoped to the caller: a retry with the same key and request replays ResourceId instead of creating again.
public class IdempotencyRecord
{
    public const int MaxKeyLength = 255;

    public string TenantId { get; private set; } = string.Empty;
    public string OwnerSubject { get; private set; } = string.Empty;
    public string Operation { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string ResourceId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedOnUtc { get; private set; }

    private IdempotencyRecord()
    {
    }

    public IdempotencyRecord(OwnerScope ownerScope, string operation, string key, string requestHash, string resourceId)
    {
        ArgumentNullException.ThrowIfNull(ownerScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        TenantId = ownerScope.TenantId;
        OwnerSubject = ownerScope.OwnerSubject;
        Operation = operation;
        Key = key;
        RequestHash = requestHash;
        ResourceId = resourceId;
    }
}
