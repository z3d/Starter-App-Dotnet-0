namespace StarterApp.Api.Application.Interfaces;

// An IQuery is an IRequest, so the mediator can always send one. A convention test checks the other direction: every request in a Queries namespace is an IQuery.
public interface IQuery<TResult> : IRequest<TResult> { }
