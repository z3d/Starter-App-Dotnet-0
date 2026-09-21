using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;

namespace StarterApp.Tests.Infrastructure;

public class CorsPolicyTests
{
    // Scope and MFA shortfalls put the machine-actionable challenge in WWW-Authenticate. A browser
    // client on an allowed origin can only read it if the policy exposes the header — in both the
    // development (any origin) and production (allow-list) branches.
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void DefaultPolicy_ExposesTheBearerChallengeAndCorrelationHeaders(string environmentName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedOrigins:0"] = "https://app.example.test" })
            .Build();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(environmentName);

        var services = new ServiceCollection();
        services.AddApiCors(configuration, environment.Object);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        var policy = options.GetPolicy(options.DefaultPolicyName);

        Assert.NotNull(policy);
        Assert.Contains("WWW-Authenticate", policy.ExposedHeaders, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("X-Correlation-ID", policy.ExposedHeaders, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Retry-After", policy.ExposedHeaders, StringComparer.OrdinalIgnoreCase);
    }
}
