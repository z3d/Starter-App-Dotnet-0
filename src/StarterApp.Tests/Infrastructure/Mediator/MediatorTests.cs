using Microsoft.Extensions.DependencyInjection;
using StarterApp.Api.Infrastructure.Mediator;
using StarterApp.Api.Infrastructure.Validation;
using MediatorImpl = StarterApp.Api.Infrastructure.Mediator.Mediator;

namespace StarterApp.Tests.Infrastructure.Mediator;

public class MediatorTests
{
    [Fact]
    public async Task SendAsync_dispatches_to_registered_handler()
    {
        var mediator = BuildMediator(s =>
            s.AddScoped<IRequestHandler<EchoRequest, string>, EchoHandler>());

        var result = await mediator.SendAsync(new EchoRequest("hello"));

        Assert.Equal("handled:hello", result);
    }

    [Fact]
    public async Task SendAsync_throws_InvalidOperationException_when_no_handler_registered()
    {
        var mediator = BuildMediator(_ => { });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => mediator.SendAsync(new OrphanRequest()));

        Assert.Contains(nameof(OrphanRequest), ex.Message);
    }

    [Fact]
    public async Task SendAsync_runs_pipeline_behaviors_in_registration_order_around_handler()
    {
        var log = new List<string>();
        var mediator = BuildMediator(s =>
        {
            s.AddSingleton(log);
            s.AddScoped<IRequestHandler<OrderedRequest, string>, OrderedHandler>();
            s.AddScoped<IPipelineBehavior<OrderedRequest, string>, OuterBehavior>();
            s.AddScoped<IPipelineBehavior<OrderedRequest, string>, InnerBehavior>();
        });

        var result = await mediator.SendAsync(new OrderedRequest());

        Assert.Equal(["outer-before", "inner-before", "handler", "inner-after", "outer-after"], log);
        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task SendAsync_throws_ValidationException_aggregating_all_validator_errors()
    {
        var mediator = BuildMediator(s =>
        {
            s.AddScoped<IRequestHandler<ValidatedRequest, string>, ValidatedHandler>();
            s.AddScoped<IValidator<ValidatedRequest>, FirstFailingValidator>();
            s.AddScoped<IValidator<ValidatedRequest>, SecondFailingValidator>();
        });

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => mediator.SendAsync(new ValidatedRequest()));

        Assert.Equal(2, ex.Errors.Count);
        Assert.Contains(ex.Errors, e => e.ErrorMessage == "first");
        Assert.Contains(ex.Errors, e => e.ErrorMessage == "second");
    }

    [Fact]
    public async Task SendAsync_propagates_cancellation_token_to_handler()
    {
        CancellationToken captured = default;
        var mediator = BuildMediator(s =>
            s.AddScoped<IRequestHandler<TokenRequest, string>>(_ =>
                new TokenCapturingHandler(ct => captured = ct)));

        using var cts = new CancellationTokenSource();
        await mediator.SendAsync(new TokenRequest(), cts.Token);

        Assert.Equal(cts.Token, captured);
    }

    [Fact]
    public async Task SendAsync_caches_wrapper_and_resolves_handler_per_call_from_DI()
    {
        // Regression guard: the wrapper is cached, but the handler must still be
        // resolved from IServiceProvider on every dispatch so scoped lifetimes and
        // re-registration work. Two different SPs with different handler instances
        // for the same request type must both dispatch correctly.
        var firstMediator = BuildMediator(s =>
            s.AddScoped<IRequestHandler<EchoRequest, string>>(_ => new EchoHandler("first")));
        var secondMediator = BuildMediator(s =>
            s.AddScoped<IRequestHandler<EchoRequest, string>>(_ => new EchoHandler("second")));

        var r1 = await firstMediator.SendAsync(new EchoRequest("x"));
        var r2 = await secondMediator.SendAsync(new EchoRequest("x"));

        Assert.Equal("first:x", r1);
        Assert.Equal("second:x", r2);
    }

    [Fact]
    public async Task SendAsync_void_dispatches_to_registered_handler()
    {
        var handler = new VoidHandler();
        var mediator = BuildMediator(s =>
            s.AddSingleton<IRequestHandler<VoidRequest>>(handler));

        await mediator.SendAsync(new VoidRequest());

        Assert.True(handler.WasCalled);
    }

    [Fact]
    public async Task SendAsync_void_runs_validators_and_throws_ValidationException_on_failure()
    {
        var handler = new VoidHandler();
        var validator = new VoidRequestValidator();
        var mediator = BuildMediator(s =>
        {
            s.AddSingleton<IRequestHandler<VoidRequest>>(handler);
            s.AddSingleton<IValidator<VoidRequest>>(validator);
        });

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => mediator.SendAsync(new VoidRequest()));

        Assert.True(validator.WasCalled);
        Assert.False(handler.WasCalled);
        Assert.Single(ex.Errors);
        Assert.Equal("void-invalid", ex.Errors[0].ErrorMessage);
    }

    private static IMediator BuildMediator(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, MediatorImpl>();
        configure(services);
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    // --- test doubles ---

    private sealed record EchoRequest(string Payload) : IRequest<string>;

    private sealed class EchoHandler : IRequestHandler<EchoRequest, string>
    {
        private readonly string _prefix;
        public EchoHandler() : this("handled") { }
        public EchoHandler(string prefix) => _prefix = prefix;
        public Task<string> HandleAsync(EchoRequest request, CancellationToken cancellationToken)
            => Task.FromResult($"{_prefix}:{request.Payload}");
    }

    private sealed record OrphanRequest : IRequest<string>;

    private sealed record OrderedRequest : IRequest<string>;

    private sealed class OrderedHandler(List<string> log) : IRequestHandler<OrderedRequest, string>
    {
        public Task<string> HandleAsync(OrderedRequest request, CancellationToken cancellationToken)
        {
            log.Add("handler");
            return Task.FromResult("ok");
        }
    }

    private sealed class OuterBehavior(List<string> log) : IPipelineBehavior<OrderedRequest, string>
    {
        public async Task<string> HandleAsync(OrderedRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            log.Add("outer-before");
            var result = await next();
            log.Add("outer-after");
            return result;
        }
    }

    private sealed class InnerBehavior(List<string> log) : IPipelineBehavior<OrderedRequest, string>
    {
        public async Task<string> HandleAsync(OrderedRequest request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            log.Add("inner-before");
            var result = await next();
            log.Add("inner-after");
            return result;
        }
    }

    private sealed record ValidatedRequest : IRequest<string>;

    private sealed class ValidatedHandler : IRequestHandler<ValidatedRequest, string>
    {
        public Task<string> HandleAsync(ValidatedRequest request, CancellationToken cancellationToken)
            => Task.FromResult("unreached");
    }

    private sealed class FirstFailingValidator : IValidator<ValidatedRequest>
    {
        public IEnumerable<ValidationError> Validate(ValidatedRequest instance)
            => [new ValidationError("x", "first")];
    }

    private sealed class SecondFailingValidator : IValidator<ValidatedRequest>
    {
        public IEnumerable<ValidationError> Validate(ValidatedRequest instance)
            => [new ValidationError("y", "second")];
    }

    private sealed record TokenRequest : IRequest<string>;

    private sealed class TokenCapturingHandler(Action<CancellationToken> capture) : IRequestHandler<TokenRequest, string>
    {
        public Task<string> HandleAsync(TokenRequest request, CancellationToken cancellationToken)
        {
            capture(cancellationToken);
            return Task.FromResult("ok");
        }
    }

    private sealed record VoidRequest : IRequest;

    private sealed class VoidHandler : IRequestHandler<VoidRequest>
    {
        public bool WasCalled { get; private set; }
        public Task HandleAsync(VoidRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class VoidRequestValidator : IValidator<VoidRequest>
    {
        public bool WasCalled { get; private set; }
        public IEnumerable<ValidationError> Validate(VoidRequest instance)
        {
            WasCalled = true;
            return [new ValidationError("VoidRequest", "void-invalid")];
        }
    }
}
