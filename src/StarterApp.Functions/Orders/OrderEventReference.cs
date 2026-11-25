using System.Text.Json;

namespace StarterApp.Functions;

public sealed record OrderEventReference(Guid OrderId, string TenantId, string OwnerSubject)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static OrderEventReference Parse(string body)
    {
        var reference = JsonSerializer.Deserialize<OrderEventReference>(body, Options);
        if (reference is null || reference.OrderId == Guid.Empty || string.IsNullOrWhiteSpace(reference.TenantId) || string.IsNullOrWhiteSpace(reference.OwnerSubject))
            throw new InvalidDataException("The order event names no order, tenant or owner.");

        return reference;
    }
}
