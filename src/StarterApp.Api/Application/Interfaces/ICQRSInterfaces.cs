namespace StarterApp.Api.Application.Interfaces;

// Marker interfaces for CQRS pattern - used for type safety and categorization
public interface ICommand { }

public interface IQuery<TResult> { }



