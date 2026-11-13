namespace StarterApp.ServiceDefaults.Payloads;

public sealed class ArtifactCaptureRequest
{
    public string ArtifactName { get; init; } = string.Empty;

    // "generated" for a final artefact, "intermediate" for one made part way through.
    public string Stage { get; init; } = ArtifactCaptureSink.GeneratedStage;
    public string? ContentType { get; init; }
    public string Payload { get; init; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } = [];
    public List<PayloadEntityReference> EntityReferences { get; init; } = [];
}
