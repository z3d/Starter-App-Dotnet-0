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


