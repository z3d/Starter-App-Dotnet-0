using Microsoft.Extensions.Caching.Distributed;
using Moq;

namespace StarterApp.Tests.Infrastructure.Caching;

public class CacheInvalidatorTests
{
    private readonly Mock<IDistributedCache> _cacheMock = new();
    private readonly CacheInvalidator _invalidator;

    public CacheInvalidatorTests()
    {
        _invalidator = new CacheInvalidator(_cacheMock.Object);
    }

    [Fact]
    public async Task InvalidateProductAsync_ShouldRemoveProductCacheKey()
    {
        await _invalidator.InvalidateProductAsync(42, CancellationToken.None);

        _cacheMock.Verify(c => c.RemoveAsync("Product:42", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateCustomerAsync_ShouldRemoveCustomerCacheKey()
    {
        await _invalidator.InvalidateCustomerAsync(7, CancellationToken.None);

        _cacheMock.Verify(c => c.RemoveAsync("Customer:7", It.IsAny<CancellationToken>()), Times.Once);
    }
}
