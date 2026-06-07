namespace StarterApp.Tests;

// Test double that records which entity keys were invalidated, so handler tests can assert that
// a stock/entity mutation actually purged the corresponding cached by-id read model.
internal sealed class RecordingCacheInvalidator : ICacheInvalidator
{
    public List<int> InvalidatedProductIds { get; } = [];
    public List<int> InvalidatedCustomerIds { get; } = [];

    public Task InvalidateProductAsync(int productId, CancellationToken cancellationToken = default)
    {
        InvalidatedProductIds.Add(productId);
        return Task.CompletedTask;
    }

    public Task InvalidateCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        InvalidatedCustomerIds.Add(customerId);
        return Task.CompletedTask;
    }
}
