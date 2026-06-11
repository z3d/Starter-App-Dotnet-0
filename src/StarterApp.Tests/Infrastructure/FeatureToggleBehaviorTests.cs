using Microsoft.Extensions.Configuration;

namespace StarterApp.Tests.Infrastructure;

public class FeatureToggleBehaviorTests
{
    [FeatureToggle("test-feature")]
    private sealed class ToggledCommand : ICommand, IRequest<string> { }

    private sealed class UntoggledCommand : ICommand, IRequest<string> { }

    private static IFeatureToggles Toggles(params (string Key, string Value)[] entries)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => $"FeatureToggles:{e.Key}", e => (string?)e.Value))
            .Build();
        return new ConfigurationFeatureToggles(configuration);
    }

    [Fact]
    public async Task ToggledOffRequest_IsRefusedBeforeTheHandlerRuns()
    {
        var behavior = new FeatureToggleBehavior<ToggledCommand, string>(Toggles(("test-feature", "false")));
        var handlerRan = false;

        var exception = await Assert.ThrowsAsync<FeatureDisabledException>(
            () => behavior.HandleAsync(new ToggledCommand(), () => { handlerRan = true; return Task.FromResult("ok"); }, CancellationToken.None));

        Assert.Equal("test-feature", exception.FeatureName);
        Assert.False(handlerRan);
    }

    [Fact]
    public async Task ToggledOnRequest_Passes()
    {
        var behavior = new FeatureToggleBehavior<ToggledCommand, string>(Toggles(("test-feature", "true")));

        Assert.Equal("ok", await behavior.HandleAsync(new ToggledCommand(), () => Task.FromResult("ok"), CancellationToken.None));
    }

    [Fact]
    public async Task MissingConfigurationEntry_DefaultsToEnabled()
    {
        var behavior = new FeatureToggleBehavior<ToggledCommand, string>(Toggles());

        Assert.Equal("ok", await behavior.HandleAsync(new ToggledCommand(), () => Task.FromResult("ok"), CancellationToken.None));
    }

    [Fact]
    public async Task UntoggledRequest_IsNeverConsulted()
    {
        var behavior = new FeatureToggleBehavior<UntoggledCommand, string>(Toggles(("test-feature", "false")));

        Assert.Equal("ok", await behavior.HandleAsync(new UntoggledCommand(), () => Task.FromResult("ok"), CancellationToken.None));
    }

    private sealed class EchoHandler : IRequestHandler<ToggledCommand, string>
    {
        public Task<string> HandleAsync(ToggledCommand command, CancellationToken cancellationToken) => Task.FromResult("ok");
    }

    [Fact]
    public async Task RealMediatorPipeline_RefusesDisabledFeature()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Toggles(("test-feature", "false")));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(FeatureToggleBehavior<,>));
        services.AddScoped<IMediator, StarterApp.Api.Infrastructure.Mediator.Mediator>();
        services.AddScoped<IRequestHandler<ToggledCommand, string>, EchoHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<FeatureDisabledException>(() => mediator.SendAsync(new ToggledCommand()));
    }
}
