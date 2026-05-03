namespace StarterApp.ServiceDefaults.Payloads;

public interface IPayloadCaptureSink
{
    Task<PayloadCaptureRecord?> CaptureAsync(PayloadCaptureRequest request, CancellationToken cancellationToken);
}
