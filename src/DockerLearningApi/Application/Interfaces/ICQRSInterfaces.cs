namespace DockerLearningApi.Application.Interfaces;

public interface ICommand { }

public interface IQuery<TResult> { }

public interface ICommandHandler<TCommand> where TCommand : ICommand
{
    Task Handle(TCommand command, CancellationToken cancellationToken = default);
}

public interface IQueryHandler<TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken cancellationToken = default);
}