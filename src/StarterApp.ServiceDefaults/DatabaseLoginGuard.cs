using Npgsql;

// Also compiled into StarterApp.DbMigrator under the DBMIGRATOR symbol, like DatabaseAuthentication.
#if DBMIGRATOR
namespace StarterApp.DbMigrator;
#else
namespace StarterApp.ServiceDefaults;
#endif

// Row-level security holds only against a login that cannot switch it off. A login is refused when it, or any role it can act as, could; with no login named, both the session's login and its current role are read, since a role set at connect hides the login behind it.
public static class DatabaseLoginGuard
{
    public const string ViolationsSql = """
        WITH login AS (SELECT oid, rolname, rolsuper, rolbypassrls, rolcreaterole FROM pg_roles WHERE rolname = @login OR (@login IS NULL AND rolname IN (session_user, current_user)))
        SELECT 'is a superuser' FROM login WHERE rolsuper
        UNION ALL
        SELECT 'may bypass row-level security' FROM login WHERE rolbypassrls
        UNION ALL
        SELECT 'may create and grant roles' FROM login WHERE rolcreaterole
        UNION ALL
        SELECT DISTINCT format('may act as %I, which owns %I.%I', owner.rolname, n.nspname, c.relname)
        FROM login, pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_roles owner ON owner.oid = c.relowner
        WHERE c.relkind IN ('r', 'p')
          AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg\_%'
          AND pg_has_role(login.oid, c.relowner, 'MEMBER')
        UNION ALL
        SELECT format('may act as %I, which can bypass row-level security', other.rolname)
        FROM login, pg_roles other
        WHERE other.oid <> login.oid AND (other.rolsuper OR other.rolbypassrls)
          AND pg_has_role(login.oid, other.oid, 'MEMBER')
        """;

    public static async Task<IReadOnlyList<string>> FindViolationsAsync(NpgsqlConnection connection, string? login, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand(ViolationsSql, connection);
        command.Parameters.Add(new NpgsqlParameter<string?>("login", NpgsqlTypes.NpgsqlDbType.Text) { TypedValue = login });
        var violations = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            violations.Add(reader.GetString(0));
        return violations;
    }

    public static InvalidOperationException Refusal(string login, IReadOnlyList<string> violations) => new(
        $"The database login {login} {string.Join("; ", violations)}, so row-level security would not hold against it. " +
        "The API and the worker sign in as their own restricted logins, which the migrator creates and grants (docs/DECISIONS.md, \"Two database logins\").");
}
