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

    [Fact]
    public async Task InvalidateProductAsync_WhenCacheThrows_ShouldNotPropagate()
    {
        _cacheMock
            .Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache offline"));

        // A transient cache outage must not turn an already-committed write into a 500.
        var exception = await Record.ExceptionAsync(() => _invalidator.InvalidateProductAsync(42, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task InvalidateProductAsync_WithAuthenticatedUser_ShouldRemoveOwnerScopedCacheKey()
    {
        var currentUser = new CurrentUser(
            "subject-1",
            AuthenticatedPrincipalType.User,
            "tenant-1",
            ["products:read"],
            "correlation",
            null,
            null,
            null);
        var invalidator = new CacheInvalidator(_cacheMock.Object, currentUser);

        await invalidator.InvalidateProductAsync(42, CancellationToken.None);

        _cacheMock.Verify(c => c.RemoveAsync("Product:42", It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.RemoveAsync(
            It.Is<string>(key => key.StartsWith("Product:42:Owner:", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
