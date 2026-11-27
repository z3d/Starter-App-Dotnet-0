namespace StarterApp.Application.Infrastructure.Identity.Session;

public sealed class IdentityProviderRefusedException : Exception
{
    public IdentityProviderRefusedException(bool refused, string message)
        : base(message)
    {
        Refused = refused;
    }

    public bool Refused { get; }
}
