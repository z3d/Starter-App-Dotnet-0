namespace StarterApp.Api.Infrastructure.Mediator;

// Void result type so commands with no return value still flow through the single
// SendAsync<TResponse> pipeline (and its IPipelineBehavior chain). There is deliberately
// no behavior-bypassing void dispatch path — every command is IRequest<T>.
public readonly record struct Unit
{
    public static Unit Value => default;
}
