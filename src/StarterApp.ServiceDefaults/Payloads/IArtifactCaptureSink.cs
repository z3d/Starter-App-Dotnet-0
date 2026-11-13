namespace StarterApp.ServiceDefaults.Payloads;

// For things the system produced, as opposed to payloads that arrived. Nothing produces one yet.
public interface IArtifactCaptureSink
{
    Task CaptureArtifactAsync(ArtifactCaptureRequest request, CancellationToken cancellationToken);

    Task CaptureBinaryArtifactAsync(string artifactName, string stage, string? contentType, byte[] content,
        IReadOnlyList<PayloadEntityReference>? entityReferences, CancellationToken cancellationToken);
}
