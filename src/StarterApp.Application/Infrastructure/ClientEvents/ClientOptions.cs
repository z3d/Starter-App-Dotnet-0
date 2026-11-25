namespace StarterApp.Application.Infrastructure.ClientEvents;

// Set per deployment. ExpectedBuild is the web image's build (the commit SHA a deploy promotes); ServiceNotice is a line an operator wants every client to show.
public class ClientOptions
{
    public const string SectionName = "Client";

    public string? ExpectedBuild { get; set; }

    public string? ServiceNotice { get; set; }
}
