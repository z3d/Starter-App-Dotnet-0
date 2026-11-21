namespace StarterApp.Api.Infrastructure.Assistant;

public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Effort { get; set; } = "low";
    public int MaxTokens { get; set; } = 4096;

    // Whether the assistant's panel starts open on a wide screen; it is always beside the screens, never instead of them.
    public bool PrimaryInterface { get; set; }

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);
}
