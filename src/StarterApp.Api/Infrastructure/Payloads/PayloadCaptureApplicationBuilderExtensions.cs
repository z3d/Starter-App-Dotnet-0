namespace StarterApp.Api.Infrastructure.Payloads;

public static class PayloadCaptureApplicationBuilderExtensions
{
    public static WebApplication UsePayloadCapture(this WebApplication app)
    {
        app.UseMiddleware<PayloadCaptureMiddleware>();
        return app;
    }
}
