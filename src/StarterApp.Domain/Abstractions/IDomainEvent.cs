namespace StarterApp.Domain.Abstractions;

public interface IDomainEvent
{
    DateTimeOffset OccurredOnUtc { get; }
}
