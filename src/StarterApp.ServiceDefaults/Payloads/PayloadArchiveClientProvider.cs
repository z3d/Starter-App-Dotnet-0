using Azure.Storage.Blobs;

namespace StarterApp.ServiceDefaults.Payloads;

// A wrapper, so the archive store can never pick up an unrelated BlobServiceClient from DI.
public sealed class PayloadArchiveClientProvider
{
    public PayloadArchiveClientProvider(BlobServiceClient? client)
    {
        Client = client;
    }

    public BlobServiceClient? Client { get; }
}
