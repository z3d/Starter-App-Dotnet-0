namespace StarterApp.DbMigrator;

// A password for a local server, or an Entra principal id for an Entra-only one; never both.
public sealed record ApplicationLogin(string Role, string Name, string? Password, string? PrincipalId)
{
    public static IReadOnlyList<ApplicationLogin> From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var logins = new List<ApplicationLogin>();
        Add("Api", ApplicationRoleSetup.ApplicationRole);
        Add("Functions", ApplicationRoleSetup.WorkerRole);
        return logins;

        void Add(string key, string role)
        {
            var section = configuration.GetSection($"DatabaseLogins:{key}");
            var name = section["Name"];
            if (string.IsNullOrWhiteSpace(name))
                return;

            var password = section["Password"];
            var principalId = section["PrincipalId"];
            if (string.IsNullOrWhiteSpace(password) == string.IsNullOrWhiteSpace(principalId))
                throw new InvalidOperationException($"DatabaseLogins:{key} needs exactly one of Password (a local server) or PrincipalId (an Entra-only server).");

            logins.Add(new ApplicationLogin(role, name, string.IsNullOrWhiteSpace(password) ? null : password, string.IsNullOrWhiteSpace(principalId) ? null : principalId));
        }
    }

    // A record prints every property, the password with them.
    public override string ToString() => $"{Name} ({Role})";
}
