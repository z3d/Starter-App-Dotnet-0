using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;

namespace StarterApp.Tests.Infrastructure.Caching;

public class CachingBehaviorTests
{
    private readonly Mock<IDistributedCache> _cacheMock = new();
    private readonly CachingBehavior<TestQuery, string> _behavior;
    private bool _nextWasCalled;

    public CachingBehaviorTests()
    {
        _behavior = new CachingBehavior<TestQuery, string>(
            _cacheMock.Object,
            CurrentUser.Anonymous,
            new LoggerFactory().CreateLogger<CachingBehavior<TestQuery, string>>());
    }

    [Fact]
    public async Task HandleAsync_WhenRequestIsNotCacheable_ShouldCallNext()
    {
        var nonCacheableRequest = new NonCacheableQuery();
        var behavior = new CachingBehavior<NonCacheableQuery, string>(
            _cacheMock.Object,
            CurrentUser.Anonymous,
            new LoggerFactory().CreateLogger<CachingBehavior<NonCacheableQuery, string>>());

        var result = await behavior.HandleAsync(nonCacheableRequest, () =>
        {
            _nextWasCalled = true;
            return Task.FromResult("from handler");
        }, CancellationToken.None);

        Assert.True(_nextWasCalled);
        Assert.Equal("from handler", result);
        _cacheMock.Verify(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenCacheHit_ShouldReturnCachedValueWithoutCallingNext()
    {
        var request = new TestQuery { Id = 42 };
        var cachedValue = JsonSerializer.Serialize("cached result");
        _cacheMock.Setup(c => c.GetAsync($"Test:{request.Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes(cachedValue));

        var result = await _behavior.HandleAsync(request, () =>
        {
            _nextWasCalled = true;
            return Task.FromResult("from handler");
        }, CancellationToken.None);

        Assert.False(_nextWasCalled);
        Assert.Equal("cached result", result);
    }

    [Fact]
    public async Task HandleAsync_WhenCacheMiss_ShouldCallNextAndStoreResult()
    {
        var request = new TestQuery { Id = 7 };
        _cacheMock.Setup(c => c.GetAsync($"Test:{request.Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var result = await _behavior.HandleAsync(request, () =>
        {
            _nextWasCalled = true;
            return Task.FromResult("fresh result");
        }, CancellationToken.None);

        Assert.True(_nextWasCalled);
        Assert.Equal("fresh result", result);

        _cacheMock.Verify(c => c.SetAsync(
            $"Test:{request.Id}",
            It.IsAny<byte[]>(),
            It.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(5)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenResultIsNull_ShouldNotCache()
    {
        var request = new NullableTestQuery();
        _cacheMock.Setup(c => c.GetAsync("NullableTest", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var behavior = new CachingBehavior<NullableTestQuery, string?>(
            _cacheMock.Object,
            CurrentUser.Anonymous,
            new LoggerFactory().CreateLogger<CachingBehavior<NullableTestQuery, string?>>());

        var result = await behavior.HandleAsync(request, () => Task.FromResult<string?>(null), CancellationToken.None);

        Assert.Null(result);
        _cacheMock.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenRequestIsOwnerScoped_ShouldVaryCacheKeyByOwner()
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
        var behavior = new CachingBehavior<OwnerScopedTestQuery, string>(
            _cacheMock.Object,
            currentUser,
            new LoggerFactory().CreateLogger<CachingBehavior<OwnerScopedTestQuery, string>>());
        var request = new OwnerScopedTestQuery { Id = 42 };
        _cacheMock.Setup(c => c.GetAsync(
                It.Is<string>(key => key.StartsWith("Test:42:Owner:", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        await behavior.HandleAsync(request, () => Task.FromResult("fresh result"), CancellationToken.None);

        _cacheMock.Verify(c => c.SetAsync(
            It.Is<string>(key => key.StartsWith("Test:42:Owner:", StringComparison.Ordinal)),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    public class TestQuery : IRequest<string>, ICacheable
    {
        public int Id { get; set; }
        public string CacheKey => $"Test:{Id}";
        public TimeSpan CacheDuration => TimeSpan.FromMinutes(5);
    }

    public class NonCacheableQuery : IRequest<string>
    {
    }

    public class NullableTestQuery : IRequest<string?>, ICacheable
    {
        public string CacheKey => "NullableTest";
        public TimeSpan CacheDuration => TimeSpan.FromMinutes(5);
    }

    public class OwnerScopedTestQuery : IRequest<string>, ICacheable, IOwnerScopedRequest
    {
        public int Id { get; set; }
        public string CacheKey => $"Test:{Id}";
        public TimeSpan CacheDuration => TimeSpan.FromMinutes(5);
    }
}
