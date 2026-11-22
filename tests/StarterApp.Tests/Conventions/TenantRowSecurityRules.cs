namespace StarterApp.Tests.Conventions;

// Selected by a general rule read from the database (every table, and whether it has a tenant_id), never by table names, so a derived project's own tables are held to it from their first migration.
public static class TenantRowSecurityRules
{
    public const string TenantColumn = "tenant_id";

    // Tables that carry no tenant's rows, each with the reason. A table with a tenant_id never belongs here.
    public static readonly IReadOnlyDictionary<string, string> PlatformTables = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["public.schemaversions"] = "the migration journal: script names and dates, read by the API's readiness check",
        ["public.outbox_messages"] = "the publish queue, drained across tenants by the outbox processor; its rows are the events every subscriber already receives from the topic",
        ["public.inbox_messages"] = "message ids a subscriber has handled, for deduplication; no tenant data",
        ["public.job_runs"] = "the scheduled jobs' run history; no tenant data",
        ["public.watched_jobs"] = "which jobs the watchdog expects to see; no tenant data",
    };

    // Policies other than the tenant policy, each a deliberate cross-tenant path for a named role.
    public static readonly IReadOnlyDictionary<string, string> CrossTenantPolicies = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["public.idempotency_records/retention_read_idempotency_records"] = "the worker's retention timer finds expired keys of every tenant",
        ["public.idempotency_records/retention_delete_idempotency_records"] = "the worker's retention timer deletes expired keys of every tenant",
    };

    public sealed record Table(string Name, bool HasTenantId, bool RowSecurity, bool ForceRowSecurity);

    public sealed record Policy(string Table, string Name, bool Permissive, IReadOnlyList<string> Roles, string Command, string? Using, string? WithCheck);

    public static IReadOnlyList<string> Violations(
        IReadOnlyList<Table> tables,
        IReadOnlyList<Policy> policies,
        IReadOnlyDictionary<string, string> platformTables,
        IReadOnlyDictionary<string, string> crossTenantPolicies)
    {
        var failures = new List<string>();

        foreach (var table in tables.OrderBy(table => table.Name, StringComparer.Ordinal))
        {
            if (!table.HasTenantId)
            {
                if (!platformTables.ContainsKey(table.Name))
                    failures.Add($"{table.Name} has no {TenantColumn}: give it one with a tenant policy, or list it in PlatformTables with the reason it holds no tenant's rows.");
                continue;
            }

            if (platformTables.ContainsKey(table.Name))
                failures.Add($"{table.Name} has a {TenantColumn} but is listed as a platform table.");
            if (!table.RowSecurity)
                failures.Add($"{table.Name} has a {TenantColumn} but row-level security is not enabled (ALTER TABLE ... ENABLE ROW LEVEL SECURITY).");
            if (!table.ForceRowSecurity)
                failures.Add($"{table.Name} has a {TenantColumn} but row-level security is not forced, so its owner reads every tenant (ALTER TABLE ... FORCE ROW LEVEL SECURITY).");
            if (!policies.Any(policy => policy.Table == table.Name && IsTenantPolicy(policy)))
                failures.Add($"{table.Name} has a {TenantColumn} but no permissive policy for all commands and all roles whose USING and WITH CHECK both compare {TenantColumn} with the session's app.tenant_id.");
        }

        foreach (var policy in policies.Where(policy => !IsTenantPolicy(policy)))
        {
            if (!crossTenantPolicies.ContainsKey($"{policy.Table}/{policy.Name}"))
                failures.Add($"{policy.Table} has policy {policy.Name}, which is not the tenant policy: list it in CrossTenantPolicies with the role it serves and why, or remove it.");
        }

        return failures;
    }

    public static IReadOnlyList<string> StaleEntries(
        IReadOnlyList<Table> tables,
        IReadOnlyList<Policy> policies,
        IReadOnlyDictionary<string, string> platformTables,
        IReadOnlyDictionary<string, string> crossTenantPolicies)
    {
        var byName = tables.ToDictionary(table => table.Name, StringComparer.Ordinal);
        var stale = platformTables.Keys
            .Where(name => !byName.TryGetValue(name, out var table) || table.HasTenantId)
            .Select(name => $"PlatformTables lists {name}, which no longer exists or now has a {TenantColumn}.")
            .ToList();
        stale.AddRange(crossTenantPolicies.Keys
            .Where(key => !policies.Any(policy => $"{policy.Table}/{policy.Name}" == key))
            .Select(key => $"CrossTenantPolicies lists {key}, which no longer exists."));
        return stale;
    }

    public static bool IsTenantPolicy(Policy policy) =>
        policy.Permissive &&
        policy.Command == "ALL" &&
        policy.Roles.SequenceEqual(["public"]) &&
        ComparesTheSessionTenant(policy.Using) &&
        ComparesTheSessionTenant(policy.WithCheck);

    private static bool ComparesTheSessionTenant(string? expression) =>
        expression is not null &&
        expression.Contains(TenantColumn, StringComparison.Ordinal) &&
        expression.Contains("current_setting('app.tenant_id'", StringComparison.Ordinal) &&
        !expression.Contains(" OR ", StringComparison.OrdinalIgnoreCase);
}
