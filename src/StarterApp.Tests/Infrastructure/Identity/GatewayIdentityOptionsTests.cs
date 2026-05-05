using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StarterApp.Api.Infrastructure;

namespace StarterApp.Tests.Infrastructure.Identity;

public class GatewayIdentityOptionsTests
{
    [Fact]
    public void AddGatewayIdentity_WithUnsignedDevelopmentInProduction_ShouldFailValidation()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["GatewayIdentity:Mode"] = "UnsignedDevelopment"
        });

        services.AddGatewayIdentity(configuration, new TestHostEnvironment("Production"));
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<GatewayIdentityOptions>>().Value);
    }

    [Fact]
    public void AddGatewayIdentity_WithRequiredModeAndMissingSigningKey_ShouldFailValidation()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["GatewayIdentity:Mode"] = "Required"
        });

        services.AddGatewayIdentity(configuration, new TestHostEnvironment("Production"));
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<GatewayIdentityOptions>>().Value);
    }

    [Fact]
    public void AddGatewayIdentity_WithRequiredModeAndSigningKey_ShouldValidate()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["GatewayIdentity:Mode"] = "Required",
            ["GatewayIdentity:SigningKey"] = "production-test-signing-key-with-at-least-32-bytes"
        });

        services.AddGatewayIdentity(configuration, new TestHostEnvironment("Production"));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<GatewayIdentityOptions>>().Value;

        Assert.Equal(GatewayIdentityMode.Required, options.Mode);
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "StarterApp.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
