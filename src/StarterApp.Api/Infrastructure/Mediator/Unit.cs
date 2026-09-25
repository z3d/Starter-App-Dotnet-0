namespace StarterApp.Api.Infrastructure.Mediator;

// Every command is IRequest<T>; there is deliberately no behaviour-bypassing void dispatch.
public readonly record struct Unit
{
    public static Unit Value => default;
}
