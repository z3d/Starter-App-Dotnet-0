using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace StarterApp.Tests.Infrastructure.Caching;

public class CachePublicationRaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleAsync_WhenInvalidationCompletesBeforePendingPublication_NeverServesTheOldValue(bool refresh)
    {
        var user = new CurrentUser("owner", AuthenticatedPrincipalType.User, "tenant", [], "correlation");
        var query = new ProductQuery();
        var key = OwnerScopedCacheKey.Create(query.CacheKey, user);
        var entries = new ConcurrentDictionary<string, byte[]>();
        if (refresh)
            entries[key] = Envelope("cached-old", DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow.AddMinutes(9));
        var cache = new Mock<IDistributedCache>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string k, CancellationToken _) => entries.GetValueOrDefault(k));
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string k, CancellationToken token) => entries.TryRemove(k, out _))
            .Returns(Task.CompletedTask);
        var publicationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumePublication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pause = true;
        cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async (string k, byte[] value, DistributedCacheEntryOptions _, CancellationToken token) =>
            {
                if (k == key && pause)
                {
                    pause = false;
                    publicationStarted.SetResult();
                    await resumePublication.Task.WaitAsync(token);
                }
                entries[k] = value;
            });
        var behavior = new CachingBehavior<ProductQuery, string>(cache.Object, user, NullLogger<CachingBehavior<ProductQuery, string>>.Instance);
        var invalidator = new CacheInvalidator(cache.Object, user, NullLogger<CacheInvalidator>.Instance);
        var pendingRead = behavior.HandleAsync(query, () => Task.FromResult("database-old"), CancellationToken.None);
        await publicationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await invalidator.InvalidateProductAsync(42, CancellationToken.None);
        resumePublication.SetResult();
        await pendingRead;

        var databaseReads = 0;
        var result = await behavior.HandleAsync(query, () =>
        {
            databaseReads++;
            return Task.FromResult("database-new");
        }, CancellationToken.None);

        Assert.Equal("database-new", result);
        Assert.Equal(1, databaseReads);
        Assert.Equal("database-new", await behavior.HandleAsync(query, () => throw new InvalidOperationException("Fresh value should be cached"), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WhenBackendReturnsAnExpiredEnvelope_IgnoresItEvenAfterTheInvalidationMarkerExpires()
    {
        var cache = new Mock<IDistributedCache>();
        cache.Setup(c => c.GetAsync("Product:42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Envelope("expired", DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow.AddSeconds(-1)));
        cache.Setup(c => c.GetAsync("Product:42:inv", It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);
        var behavior = new CachingBehavior<ProductQuery, string>(cache.Object, CurrentUser.Anonymous, NullLogger<CachingBehavior<ProductQuery, string>>.Instance);

        var result = await behavior.HandleAsync(new ProductQuery(), () => Task.FromResult("fresh"), CancellationToken.None);

        Assert.Equal("fresh", result);
    }

    private static byte[] Envelope(string value, DateTimeOffset refreshAfterUtc, DateTimeOffset expiresAtUtc)
        => JsonSerializer.SerializeToUtf8Bytes(new { Value = value, RefreshAfterUtc = refreshAfterUtc, ExpiresAtUtc = expiresAtUtc, Generation = (string?)null });

    public sealed class ProductQuery : IRequest<string>, ICacheable, IOwnerScopedRequest
    {
        public string CacheKey => "Product:42";
        public TimeSpan CacheDuration => TimeSpan.FromMinutes(10);
        public TimeSpan CacheRefreshWindow => TimeSpan.FromMinutes(1);
    }
}
