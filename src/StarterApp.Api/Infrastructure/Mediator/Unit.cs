namespace StarterApp.Api.Infrastructure.Mediator;

// Every command is an IRequest<T>. There is no void dispatch on purpose, because it would skip the behaviours.
public readonly record struct Unit
{
    public static Unit Value => default;
}
