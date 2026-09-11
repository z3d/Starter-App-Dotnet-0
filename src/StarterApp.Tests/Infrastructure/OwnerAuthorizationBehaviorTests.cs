namespace StarterApp.Tests.Infrastructure;

public class OwnerAuthorizationBehaviorTests
{
    private sealed class MarkedCommand : ICommand, IRequest<string>, IOwnerAuthorizedMutation { }

    private sealed class UnmarkedCommand : ICommand, IRequest<string> { }

    [Fact]
    public async Task MarkedCommand_WithoutPolicyEvaluation_Throws()
    {
        var behavior = new OwnerAuthorizationBehavior<MarkedCommand, string>(new OwnerPolicyEvaluationTracker());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => behavior.HandleAsync(new MarkedCommand(), () => Task.FromResult("ok"), CancellationToken.None));

        Assert.Contains("MarkedCommand", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IOwnerOnlyPolicy", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkedCommand_FlagsTheTrackerBeforeTheHandlerRuns()
    {
        var tracker = new OwnerPolicyEvaluationTracker();
        var behavior = new OwnerAuthorizationBehavior<MarkedCommand, string>(tracker);
        var requiredDuringHandler = false;

        await behavior.HandleAsync(
            new MarkedCommand(),
            () =>
            {
                requiredDuringHandler = tracker.RequiresEvaluation;
                tracker.MarkEvaluated();
                return Task.FromResult("ok");
            },
            CancellationToken.None);

        Assert.True(requiredDuringHandler);
    }

    [Fact]
    public async Task MarkedCommand_WithPolicyEvaluation_Succeeds()
    {
        var tracker = new OwnerPolicyEvaluationTracker();
        var behavior = new OwnerAuthorizationBehavior<MarkedCommand, string>(tracker);

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
        var tracker = new OwnerPolicyEvaluationTracker();
        var behavior = new OwnerAuthorizationBehavior<UnmarkedCommand, string>(tracker);

        var response = await behavior.HandleAsync(new UnmarkedCommand(), () => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", response);
        Assert.False(tracker.RequiresEvaluation);
    }

    [Fact]
    public async Task MarkedCommand_WhoseHandlerThrows_PropagatesWithoutEnforcementNoise()
    {
        var behavior = new OwnerAuthorizationBehavior<MarkedCommand, string>(new OwnerPolicyEvaluationTracker());

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
