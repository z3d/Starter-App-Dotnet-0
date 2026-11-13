namespace StarterApp.ServiceDefaults.Payloads;

public sealed record PendingPayloadAppend(string BlobName, string Line);
