namespace StarterApp.Api.Infrastructure.Idempotency;

public sealed class IdempotencyKeyReusedException : Exception
{
    public IdempotencyKeyReusedException()
    {
    }

    public IdempotencyKeyReusedException(string message)
        : base(message)
    {
    }

    public IdempotencyKeyReusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
