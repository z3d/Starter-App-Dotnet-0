namespace StarterApp.Api.Application.Interfaces;

public interface ICommand { }

// An IQuery is an IRequest, so the mediator can always send one. A convention test checks the other direction: every request in a Queries namespace is an IQuery.
public interface IQuery<TResult> : IRequest<TResult> { }

public interface IOwnerScopedRequest { }

// A request with this marker must have its handler call IOwnerOnlyPolicy.Authorize. If it does not, OwnerAuthorizationBehavior fails the request.
public interface IOwnerAuthorizedMutation { }

// If the FeatureToggles:{Name} setting is off, the mediator answers 503 before any validator or behaviour runs. A disabled feature is never served from the cache.
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


