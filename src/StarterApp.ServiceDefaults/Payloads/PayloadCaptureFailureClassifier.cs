using Azure;
using System.Net.Sockets;

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
        return status is 408 or 429 || status >= 500;
    }
}
