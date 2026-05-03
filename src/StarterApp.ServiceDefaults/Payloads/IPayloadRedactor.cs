namespace StarterApp.ServiceDefaults.Payloads;

public interface IPayloadRedactor
{
    string Redact(string payload, string? contentType = null);
}
