using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace StarterApp.Tests.Infrastructure;

public class RateLimitingTests
{
    [Fact]
    public void PartitionKey_ForAuthenticatedUser_IsVerifiedTenantAndSubject()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new CurrentUser(
            "subject-9", AuthenticatedPrincipalType.User, "tenant-9",
            ["products:read"], "case-rl"));
        using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };

        var key = StarterApp.Api.Infrastructure.ServiceCollectionExtensions.ResolveRateLimitPartitionKey(context);

        Assert.Equal("identity:tenant-9:subject-9", key);
    }

    [Fact]
    public void PartitionKey_ForAnonymousTraffic_FallsBackToClientIp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(CurrentUser.Anonymous);
        using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");

        var key = StarterApp.Api.Infrastructure.ServiceCollectionExtensions.ResolveRateLimitPartitionKey(context);

        Assert.Equal("ip:203.0.113.7", key);
    }

    [Fact]
    public void Options_BindFromConfiguration_AndCarryReviewedDefaults()
    {
        var bound = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:PermitLimit"] = "250",
                ["RateLimiting:WindowSeconds"] = "30",
                ["RateLimiting:QueueLimit"] = "0"
            })
            .Build()
            .GetSection(RateLimitingOptions.SectionName)
            .Get<RateLimitingOptions>()!;

        Assert.Equal(250, bound.PermitLimit);
        Assert.Equal(30, bound.WindowSeconds);
        Assert.Equal(0, bound.QueueLimit);

        var defaults = new RateLimitingOptions();
        Assert.Equal(100, defaults.PermitLimit);
        Assert.Equal(60, defaults.WindowSeconds);
        Assert.Equal(5, defaults.QueueLimit);
    }

    [Fact]
    public void Options_OutOfRangeValues_FailValidationAtStartup()
    {
        var services = new ServiceCollection();
        services.AddOptions<RateLimitingOptions>()
            .Configure(o => o.PermitLimit = 0)
            .ValidateDataAnnotations();
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RateLimitingOptions>>().Value);
    }
}
