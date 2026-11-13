namespace StarterApp.Api.Application.Interfaces;

// A request with this marker must have its handler call IOwnerOnlyPolicy.Authorize. If it does not, OwnerAuthorizationBehavior fails the request.
public interface IOwnerAuthorizedMutation { }
