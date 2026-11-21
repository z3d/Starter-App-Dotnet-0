using Microsoft.Extensions.Options;
using StarterApp.Api.Infrastructure.ClientEvents;

namespace StarterApp.Tests.Infrastructure;

public class ClientBuildMiddlewareTests
{
    [Theory]
    [InlineData("abc123", "def456", true)]
    [InlineData("abc123", "ABC123", false)]
    [InlineData("abc123", null, false)]
    [InlineData("abc123", "", false)]
    [InlineData("dev", "def456", false)]
    [InlineData(null, "def456", false)]
    public void IsStale_OnlyWhenANamedBuildDiffersFromTheDeployedOne(string? build, string? expected, bool stale)
    {
        Assert.Equal(stale, ClientBuildMiddleware.IsStale(build, expected));
    }

    [Theory]
    [InlineData("3f2a9c1", "3f2a9c1")]
    [InlineData("v1.2.3-rc_1", "v1.2.3-rc_1")]
    [InlineData("bad build", null)]
    [InlineData("<script>", null)]
    [InlineData("", null)]
    public void Sanitize_KeepsOnlyABuildShapedValue(string value, string? expected)
    {
        Assert.Equal(expected, ClientBuildMiddleware.Sanitize(value));
    }

    [Fact]
    public async Task AStaleBuild_IsToldToReload_AndTheNoticeRidesAlong()
    {
        var options = new ClientOptions { ExpectedBuild = "new-build", ServiceNotice = "Payments are slow today" };
        var middleware = new ClientBuildMiddleware(context => context.Response.WriteAsync("ok"), new StaticMonitor(options));
        var context = new DefaultHttpContext();
        context.Request.Headers[ClientBuildMiddleware.BuildHeader] = "old-build";
        var responseFeature = new StartableResponseFeature();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(responseFeature);
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);
        await responseFeature.StartAsync();

        Assert.Equal("true", context.Response.Headers[ClientBuildMiddleware.ReloadHeader].ToString());
        Assert.Equal("Payments are slow today", Uri.UnescapeDataString(context.Response.Headers[ClientBuildMiddleware.NoticeHeader].ToString()));
    }

    private sealed class StaticMonitor(ClientOptions value) : IOptionsMonitor<ClientOptions>
    {
        public ClientOptions CurrentValue => value;
        public ClientOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<ClientOptions, string?> listener) => null;
    }

    private sealed class StartableResponseFeature : Microsoft.AspNetCore.Http.Features.HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _starting = [];

        public override void OnStarting(Func<object, Task> callback, object state) => _starting.Add((callback, state));

        public async Task StartAsync()
        {
            foreach (var (callback, state) in _starting)
                await callback(state);
        }
    }
}
