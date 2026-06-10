using Azure;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Infrastructure.Payloads;

public class PayloadCaptureFailureClassifierTests
{
    [Theory]
    [InlineData(0)]   // DNS / connection refused — no HTTP response was ever produced
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void RequestFailedException_WithTransientStatus_IsTransient(int status)
    {
        Assert.True(PayloadCaptureFailureClassifier.IsTransientDependencyFailure(new RequestFailedException(status, "boom")));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(404)]
    public void RequestFailedException_WithClientErrorStatus_IsNotTransient(int status)
    {
        Assert.False(PayloadCaptureFailureClassifier.IsTransientDependencyFailure(new RequestFailedException(status, "boom")));
    }

    [Fact]
    public void NonTransientException_WithTransientInnerException_IsTransient()
    {
        var exception = new InvalidOperationException("wrapper", new System.Net.Sockets.SocketException());

        Assert.True(PayloadCaptureFailureClassifier.IsTransientDependencyFailure(exception));
    }
}
