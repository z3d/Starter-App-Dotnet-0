using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace StarterApp.Tests.Infrastructure.Caching;

public class CacheInvalidatorTests
{
    private static readonly CurrentUser AuthenticatedUser = new(
        "subject-1",
        AuthenticatedPrincipalType.User,
        "tenant-1",
        ["products:read"],
        "correlation");

    private readonly Mock<IDistributedCache> _cacheMock = new();

    private CacheInvalidator CreateInvalidator(ICurrentUser currentUser) =>
        new(_cacheMock.Object, currentUser, NullLogger<CacheInvalidator>.Instance);

    [Fact]
    public async Task InvalidateProductAsync_EveryMutationChangesTheGeneration()
    {
        var values = new List<string>();
        _cacheMock.Setup(cache => cache.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Callback((string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token) =>
                values.Add(System.Text.Encoding.UTF8.GetString(value)))
            .Returns(Task.CompletedTask);
        var invalidator = CreateInvalidator(AuthenticatedUser);
        await invalidator.InvalidateProductAsync(42, CancellationToken.None);
        await invalidator.InvalidateProductAsync(42, CancellationToken.None);
        Assert.Equal(2, values.Count);
        Assert.NotEqual(values[0], values[1]);
    }

    [Fact]
    public async Task InvalidateProductAsync_RemovesOnlyTheOwnerScopedKey()
    {
        var invalidator = CreateInvalidator(AuthenticatedUser);

        await invalidator.InvalidateProductAsync(42, CancellationToken.None);

        // The bare key has no writer (cacheable queries are owner-scoped and unreachable
        // without an authenticated identity), so it must not be touched.
        _cacheMock.Verify(c => c.RemoveAsync(
            It.Is<string>(key => key.StartsWith("Product:42:Owner:", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.RemoveAsync("Product:42", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvalidateCustomerAsync_RemovesOnlyTheOwnerScopedKey()
    {
        var invalidator = CreateInvalidator(AuthenticatedUser);

        await invalidator.InvalidateCustomerAsync(7, CancellationToken.None);

        _cacheMock.Verify(c => c.RemoveAsync(
            It.Is<string>(key => key.StartsWith("Customer:7:Owner:", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.RemoveAsync("Customer:7", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvalidateProductAsync_WithoutIdentity_IsANoOp()
    {
        var invalidator = CreateInvalidator(CurrentUser.Anonymous);

        await invalidator.InvalidateProductAsync(42, CancellationToken.None);

        _cacheMock.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvalidateProductAsync_WhenTheGenerationWriteFails_StillEvictsTheEntry()
    {
        // The generation advance and the eviction are independent defences. If the first fails
        // and the second is skipped, a deleted row keeps being served for its whole cache duration.
        _cacheMock
            .Setup(c => c.SetAsync(It.Is<string>(key => key.EndsWith(":inv", StringComparison.Ordinal)), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache offline"));
        var invalidator = CreateInvalidator(AuthenticatedUser);

        await invalidator.InvalidateProductAsync(42, CancellationToken.None);

        _cacheMock.Verify(c => c.RemoveAsync(
            It.Is<string>(key => key.StartsWith("Product:42:Owner:", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateProductAsync_WhenCacheThrows_ShouldNotPropagate()
    {
        _cacheMock
            .Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache offline"));
        var invalidator = CreateInvalidator(AuthenticatedUser);

        // A transient cache outage must not turn an already-committed write into a 500.
        var exception = await Record.ExceptionAsync(() => invalidator.InvalidateProductAsync(42, CancellationToken.None));

        Assert.Null(exception);
    }
}
