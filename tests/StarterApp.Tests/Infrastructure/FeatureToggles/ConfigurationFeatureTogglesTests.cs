using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace StarterApp.Tests.Infrastructure.FeatureToggles;

public class ConfigurationFeatureTogglesTests
{
    [Fact]
    public void AToggleWithNoEntry_IsOff()
    {
        Assert.False(Toggles().IsEnabled("order-search"));
        Assert.False(Toggles(("FeatureToggles:another-feature", "true")).IsEnabled("order-search"));
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    public void AConfiguredToggle_IsWhatItsEntrySays(string value, bool expected)
    {
        Assert.Equal(expected, Toggles(("FeatureToggles:order-search", value)).IsEnabled("order-search"));
    }

    [Fact]
    public async Task AKeyNoRequestDeclares_RefusesStartUp_AndNamesItselfAndTheKnownToggles()
    {
        var refusal = await Assert.ThrowsAsync<OptionsValidationException>(() => StartAsync(("FeatureToggles:order-searc", "false")));

        Assert.Contains("order-searc", refusal.Message);
        Assert.Contains("Known toggles: ", refusal.Message);
        Assert.Contains("order-search", refusal.Message.Split("Known toggles: ")[1]);
    }

    [Theory]
    [InlineData("FeatureToggles:order-search")]
    [InlineData("FeatureToggles:Order-Search")]
    public async Task ADeclaredKey_InAnyCase_StartsUp(string key)
    {
        await StartAsync((key, "false"));
    }

    [Fact]
    public void AnEntryThatIsNotTrueOrFalse_IsRefused_NotReadAsOn()
    {
        Assert.Throws<InvalidOperationException>(() => Toggles(("FeatureToggles:order-search", "off")).IsEnabled("order-search"));
    }

    [Theory]
    [InlineData("off")]
    [InlineData("1")]
    [InlineData("")]
    public async Task AnEntryThatIsNotTrueOrFalse_RefusesStartUp_AndNamesTheKey(string value)
    {
        var refusal = await Assert.ThrowsAsync<OptionsValidationException>(() => StartAsync(("FeatureToggles:order-search", value)));

        Assert.Contains("order-search", refusal.Message);
        Assert.Contains("not true or false", refusal.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ABlankName_IsRefused(string name)
    {
        Assert.Throws<ArgumentException>(() => Toggles().IsEnabled(name));
    }

    private static ConfigurationFeatureToggles Toggles(params (string Key, string Value)[] entries) =>
        new(Configuration(entries));

    private static async Task StartAsync(params (string Key, string Value)[] entries)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddConfiguration(Configuration(entries));
        builder.Services.AddMediator(typeof(ConfigurationFeatureTogglesTests).Assembly);
        using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();
    }

    private static IConfiguration Configuration((string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)))
            .Build();

    [FeatureToggle("order-search")]
    private sealed record ToggleProbe : IRequest<string>;
}
