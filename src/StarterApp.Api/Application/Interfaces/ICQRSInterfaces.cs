namespace StarterApp.Api.Application.Interfaces;

// Marker interfaces for CQRS pattern - used for type safety and categorization
public interface ICommand { }

public interface IQuery<TResult> { }

public interface IOwnerScopedRequest { }

// Non-create commands mutate an existing owner-scoped aggregate and must carry this
// marker. OwnerAuthorizationBehavior verifies after the handler completes that
// IOwnerOnlyPolicy.Authorize actually ran — injection alone is not invocation.
// Convention tests keep the cohort complete.
public interface IOwnerAuthorizedMutation { }

// Declares a kill-switch/dark-launch toggle on a request TYPE (command or query).
// FeatureToggleBehavior refuses dispatch with a FeatureDisabledException (503) when
// configuration sets FeatureToggles:{Name} to false. Convention tests enforce:
// request types only, unique names, and an explicit configuration entry per name.
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


