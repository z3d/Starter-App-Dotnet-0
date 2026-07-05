using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace StarterApp.Tests.Infrastructure;

public class OwnerAuthorizationBehaviorTests
{
    private sealed class MarkedCommand : ICommand, IRequest<string>, IOwnerAuthorizedMutation { }

    private sealed class UnmarkedCommand : ICommand, IRequest<string> { }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "StarterApp.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static OwnerAuthorizationBehavior<TRequest, string> CreateBehavior<TRequest>(
        OwnerPolicyEvaluationTracker tracker, string environment)
        where TRequest : IRequest<string>
        => new(tracker, new FakeHostEnvironment(environment));

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task MarkedCommand_WithoutPolicyEvaluation_Throws_InDevAndTesting(string environment)
    {
        var behavior = CreateBehavior<MarkedCommand>(new OwnerPolicyEvaluationTracker(), environment);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => behavior.HandleAsync(new MarkedCommand(), () => Task.FromResult("ok"), CancellationToken.None));

        Assert.Contains("MarkedCommand", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IOwnerOnlyPolicy", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkedCommand_WithoutPolicyEvaluation_DoesNotFailRequest_InProduction()
    {
        var behavior = CreateBehavior<MarkedCommand>(new OwnerPolicyEvaluationTracker(), "Production");

        var response = await behavior.HandleAsync(new MarkedCommand(), () => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", response);
    }

    [Fact]
    public async Task MarkedCommand_WithPolicyEvaluation_Succeeds()
    {
        var tracker = new OwnerPolicyEvaluationTracker();
        var behavior = CreateBehavior<MarkedCommand>(tracker, "Testing");

        var response = await behavior.HandleAsync(
            new MarkedCommand(),
            () =>
            {
                tracker.MarkEvaluated();
                return Task.FromResult("ok");
            },
            CancellationToken.None);

        Assert.Equal("ok", response);
    }

    [Fact]
    public async Task UnmarkedCommand_WithoutPolicyEvaluation_Succeeds()
    {
        var behavior = CreateBehavior<UnmarkedCommand>(new OwnerPolicyEvaluationTracker(), "Testing");

        var response = await behavior.HandleAsync(new UnmarkedCommand(), () => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", response);
    }

    [Fact]
    public async Task MarkedCommand_WhoseHandlerThrows_PropagatesWithoutEnforcementNoise()
    {
        var behavior = CreateBehavior<MarkedCommand>(new OwnerPolicyEvaluationTracker(), "Testing");

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => behavior.HandleAsync(new MarkedCommand(), () => Task.FromException<string>(new EntityNotFoundException("not found")), CancellationToken.None));
    }

    private sealed class SkippingHandler : IRequestHandler<MarkedCommand, string>
    {
        public Task<string> HandleAsync(MarkedCommand command, CancellationToken cancellationToken) => Task.FromResult("ok");
    }

    private sealed class AuthorizingHandler : IRequestHandler<MarkedCommand, string>
    {
        private readonly OwnerPolicyEvaluationTracker _tracker;

        public AuthorizingHandler(OwnerPolicyEvaluationTracker tracker) => _tracker = tracker;

        public Task<string> HandleAsync(MarkedCommand command, CancellationToken cancellationToken)
        {
            _tracker.MarkEvaluated();
            return Task.FromResult("ok");
        }
    }

    [Fact]
    public async Task RealMediatorPipeline_CatchesHandlerThatSkipsThePolicy()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment("Testing"));
        services.AddScoped<OwnerPolicyEvaluationTracker>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(OwnerAuthorizationBehavior<,>));
        services.AddScoped<IMediator, StarterApp.Api.Infrastructure.Mediator.Mediator>();
        services.AddScoped<IRequestHandler<MarkedCommand, string>, SkippingHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.SendAsync(new MarkedCommand()));
    }

    [Fact]
    public async Task RealMediatorPipeline_PassesHandlerThatConsultsThePolicy()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment("Testing"));
        services.AddScoped<OwnerPolicyEvaluationTracker>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(OwnerAuthorizationBehavior<,>));
        services.AddScoped<IMediator, StarterApp.Api.Infrastructure.Mediator.Mediator>();
        services.AddScoped<IRequestHandler<MarkedCommand, string>, AuthorizingHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        Assert.Equal("ok", await mediator.SendAsync(new MarkedCommand()));
    }
}
