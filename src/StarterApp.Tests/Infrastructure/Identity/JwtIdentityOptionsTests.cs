using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;

namespace StarterApp.Tests.Infrastructure.Identity;

public class JwtIdentityOptionsTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void MissingAuthority_FailsValidation_OutsideDevelopmentLikeEnvironments(string environmentName)
    {
        var provider = BuildProvider(environmentName, new Dictionary<string, string?>
        {
            ["Identity:Audience"] = "starterapp-api"
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<JwtIdentityOptions>>().Value);
        Assert.Contains("Identity:Authority is required", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void MissingAuthority_IsAllowed_InDevelopmentLikeEnvironments(string environmentName)
    {
        var provider = BuildProvider(environmentName, new Dictionary<string, string?>
        {
            ["Identity:Audience"] = "starterapp-api"
        });

        var options = provider.GetRequiredService<IOptions<JwtIdentityOptions>>().Value;
        Assert.Equal("starterapp-api", options.Audience);
    }

    [Fact]
    public void PlainHttpMetadata_FailsValidation_OutsideDevelopmentLikeEnvironments()
    {
        var provider = BuildProvider("Production", new Dictionary<string, string?>
        {
            ["Identity:Authority"] = "https://idp.example.com/realms/starterapp",
            ["Identity:Audience"] = "starterapp-api",
            ["Identity:RequireHttpsMetadata"] = "false"
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<JwtIdentityOptions>>().Value);
        Assert.Contains("RequireHttpsMetadata=false is only allowed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingAudience_FailsValidation_Everywhere()
    {
        var provider = BuildProvider("Testing", new Dictionary<string, string?>());

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<JwtIdentityOptions>>().Value);
    }

    private static ServiceProvider BuildProvider(string environmentName, Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(environmentName);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddJwtIdentity(configuration, environment.Object);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("realms/starterapp")]
    [InlineData("not a uri")]
    [InlineData("ftp://idp.example.com/realms/starterapp")]
    public void MalformedAuthority_FailsValidation(string authority)
    {
        var provider = BuildProvider("Production", new Dictionary<string, string?>
        {
            ["Identity:Authority"] = authority,
            ["Identity:Audience"] = "starterapp-api"
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<JwtIdentityOptions>>().Value);
        Assert.Contains("absolute http(s) URI", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HttpAuthority_FailsValidation_WhenHttpsMetadataIsRequired()
    {
        // Development still defaults RequireHttpsMetadata to true, so an http authority there is a
        // misconfiguration too — the local Keycloak setup sets the flag explicitly.
        var provider = BuildProvider("Development", new Dictionary<string, string?>
        {
            ["Identity:Authority"] = "http://localhost:8080/realms/starterapp",
            ["Identity:Audience"] = "starterapp-api"
        });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<JwtIdentityOptions>>().Value);
    }

    [Fact]
    public void HttpAuthority_IsAllowed_WhenHttpsMetadataIsNotRequired_InDevelopment()
    {
        var provider = BuildProvider("Development", new Dictionary<string, string?>
        {
            ["Identity:Authority"] = "http://localhost:8080/realms/starterapp",
            ["Identity:Audience"] = "starterapp-api",
            ["Identity:RequireHttpsMetadata"] = "false"
        });

        Assert.Equal("http://localhost:8080/realms/starterapp",
            provider.GetRequiredService<IOptions<JwtIdentityOptions>>().Value.Authority);
    }

    [Fact]
    public void HttpsAuthority_PassesValidation_InProduction()
    {
        var provider = BuildProvider("Production", new Dictionary<string, string?>
        {
            ["Identity:Authority"] = "https://idp.example.com/realms/starterapp",
            ["Identity:Audience"] = "starterapp-api"
        });

        Assert.Equal("https://idp.example.com/realms/starterapp",
            provider.GetRequiredService<IOptions<JwtIdentityOptions>>().Value.Authority);
    }
}
