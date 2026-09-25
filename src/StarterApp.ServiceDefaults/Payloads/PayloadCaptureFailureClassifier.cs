using System.Net.Sockets;
using Azure;

namespace StarterApp.ServiceDefaults.Payloads;

public static class PayloadCaptureFailureClassifier
{
    public static bool IsTransientDependencyFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            RequestFailedException requestFailedException => IsTransientStatus(requestFailedException.Status),
            HttpRequestException => true,
            IOException => true,
            SocketException => true,
            TimeoutException => true,
            _ when exception.InnerException is not null => IsTransientDependencyFailure(exception.InnerException),
            _ => false
        };
    }

    private static bool IsTransientStatus(int status)
    {
        // Status 0 is the SDK's marker for failures with no HTTP response (DNS, refused, TLS): the common transient shape.
        return status is 0 or 408 or 429 || status >= 500;
    }
}
