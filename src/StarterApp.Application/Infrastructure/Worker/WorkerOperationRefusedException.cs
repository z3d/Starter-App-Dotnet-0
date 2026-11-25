namespace StarterApp.Application.Infrastructure.Worker;

// A refusal a redelivery cannot change (the request, the tenant or the row is wrong), so the message is dead-lettered for replay once fixed.
public sealed class WorkerOperationRefusedException : Exception
{
    public WorkerOperationRefusedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
