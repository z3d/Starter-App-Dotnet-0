namespace StarterApp.Api.Application.Interfaces;

public interface ICommand { }

// IQuery extends IRequest so every query is dispatchable by construction; the reverse rule is convention-tested.
public interface IQuery<TResult> : IRequest<TResult> { }

public interface IOwnerScopedRequest { }

// OwnerAuthorizationBehavior fails a request carrying this marker whose handler never called IOwnerOnlyPolicy.Authorize.
public interface IOwnerAuthorizedMutation { }

// The mediator refuses a disabled toggle with 503 before validators and every behaviour, so it is never served from cache.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class FeatureToggleAttribute : Attribute
{
    public FeatureToggleAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }
}


