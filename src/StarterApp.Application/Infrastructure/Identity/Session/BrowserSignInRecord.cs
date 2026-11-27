namespace StarterApp.Application.Infrastructure.Identity.Session;

public sealed class BrowserSignInRecord
{
    public string CodeVerifier { get; set; } = string.Empty;

    public string BrowserBinding { get; set; } = string.Empty;

    public string ReturnTo { get; set; } = "/";
}
