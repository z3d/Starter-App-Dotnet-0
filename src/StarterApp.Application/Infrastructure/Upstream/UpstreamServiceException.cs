namespace StarterApp.Application.Infrastructure.Upstream;

// Thrown by an outbound client once its resilience handler has spent its retries. Transient answers 502; permanent stays a 500, since our request or configuration is wrong.
public sealed class UpstreamServiceException : Exception
{
    public UpstreamServiceException(string service, bool isPermanent, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Service = service;
        IsPermanent = isPermanent;
    }

    public string Service { get; }

    public bool IsPermanent { get; }

    public static UpstreamServiceException Unreachable(string service, Exception innerException) =>
        new(service, isPermanent: false, $"The outside service '{service}' could not be reached after its retries.", innerException);
}
