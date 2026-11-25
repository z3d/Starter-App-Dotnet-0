using System.Text.Json;
using Npgsql;

namespace StarterApp.DbMigrator;

// Runs after the scripts on every migration, so a new table is granted without its script having to remember; the grants are restated every run and nothing here is journalled.
public static class ApplicationRoleSetup
{
    public const string ApplicationRole = "starterapp_app";
    public const string WorkerRole = "starterapp_worker";

    public static readonly IReadOnlyList<string> Roles = [ApplicationRole, WorkerRole];

    private const string LoginsSetting = "starterapp.application_logins";

    // The worker holds only what it uses; a request added to IWorkerOperations adds the tables it reads or writes (a save also writes outbox_messages), and WorkerOperationsTests runs it on this login.
    public static readonly IReadOnlyDictionary<string, string> WorkerGrants = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["public.inbox_messages"] = "SELECT, INSERT, DELETE",
        ["public.job_runs"] = "SELECT, INSERT, UPDATE, DELETE",
        ["public.watched_jobs"] = "SELECT, INSERT",
        ["public.idempotency_records"] = "SELECT, DELETE",
        ["public.orders"] = "SELECT",
        ["public.order_items"] = "SELECT",
        ["public.customers"] = "SELECT",
    };

    private const string WorkerGrantsSetting = "starterapp.worker_grants";

    public const string GrantSql = $$"""
        DO $grants$
        DECLARE
            target record;
            role_name text;
            worker_grant record;
            sequence_name text;
        BEGIN
            FOREACH role_name IN ARRAY ARRAY['{{ApplicationRole}}', '{{WorkerRole}}'] LOOP
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = role_name) THEN
                    RAISE EXCEPTION 'Role % does not exist; the migration that creates it has not run', role_name;
                END IF;

                FOR target IN
                    SELECT n.nspname
                    FROM pg_namespace n
                    WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
                      AND n.nspname NOT LIKE 'pg\_%'
                      AND EXISTS (SELECT 1 FROM pg_class c WHERE c.relnamespace = n.oid AND c.relkind IN ('r', 'p', 'S'))
                LOOP
                    EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', target.nspname, role_name);
                    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I', target.nspname, role_name);
                    EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO %I', target.nspname, role_name);
                END LOOP;

                IF to_regclass('public.schemaversions') IS NOT NULL THEN
                    EXECUTE format('REVOKE ALL ON public.schemaversions FROM %I', role_name);
                END IF;
            END LOOP;

            -- The worker keeps only its listed tables; anything granted before, or by the loop above, is taken back first.
            FOR target IN
                SELECT n.nspname FROM pg_namespace n
                WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg\_%'
            LOOP
                EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA %I FROM {{WorkerRole}}', target.nspname);
                EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA %I FROM {{WorkerRole}}', target.nspname);
            END LOOP;

            FOR worker_grant IN SELECT key AS table_name, value AS privileges FROM jsonb_each_text(current_setting('{{WorkerGrantsSetting}}')::jsonb) LOOP
                IF to_regclass(worker_grant.table_name) IS NOT NULL THEN
                    EXECUTE format('GRANT %s ON %s TO {{WorkerRole}}', worker_grant.privileges, worker_grant.table_name);
                    FOR sequence_name IN
                        SELECT s.oid::regclass::text FROM pg_class s
                        JOIN pg_depend d ON d.objid = s.oid AND d.deptype IN ('a', 'i')
                        WHERE s.relkind = 'S' AND d.refobjid = to_regclass(worker_grant.table_name)
                    LOOP
                        EXECUTE format('GRANT USAGE, SELECT ON SEQUENCE %s TO {{WorkerRole}}', sequence_name);
                    END LOOP;
                END IF;
            END LOOP;

            -- The API's readiness check compares the journal with the scripts it was built with.
            IF to_regclass('public.schemaversions') IS NOT NULL THEN
                GRANT SELECT ON public.schemaversions TO {{ApplicationRole}};
            END IF;
        END
        $grants$;
        """;

    public const string LoginSql = $$"""
        DO $logins$
        DECLARE
            login record;
        BEGIN
            FOR login IN
                SELECT l.role, l.name, l.password, l.principal_id
                FROM jsonb_to_recordset(current_setting('{{LoginsSetting}}')::jsonb) AS l(role text, name text, password text, principal_id text)
            LOOP
                IF login.role NOT IN ('{{ApplicationRole}}', '{{WorkerRole}}') THEN
                    RAISE EXCEPTION 'Login % names role %, which is not an application role', login.name, login.role;
                END IF;

                IF login.principal_id IS NOT NULL THEN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = login.name) THEN
                        PERFORM pgaadauth_create_principal_with_oid(login.name, login.principal_id, 'service', false, false);
                    END IF;
                ELSIF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = login.name) THEN
                    EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L', login.name, login.password);
                ELSE
                    EXECUTE format('ALTER ROLE %I LOGIN PASSWORD %L', login.name, login.password);
                END IF;

                EXECUTE format('GRANT %I TO %I', login.role, login.name);
            END LOOP;
        END
        $logins$;
        """;

    public static async Task ApplyAsync(string connectionString, IReadOnlyList<ApplicationLogin> logins, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logins);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var list = new NpgsqlCommand($"SELECT set_config('{WorkerGrantsSetting}', @grants, true)", connection, transaction))
        {
            list.Parameters.AddWithValue("grants", JsonSerializer.Serialize(WorkerGrants));
            await list.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var grants = new NpgsqlCommand(GrantSql, connection, transaction))
            await grants.ExecuteNonQueryAsync(cancellationToken);

        await using (var list = new NpgsqlCommand($"SELECT set_config('{LoginsSetting}', @logins, true)", connection, transaction))
        {
            list.Parameters.AddWithValue("logins", JsonSerializer.Serialize(logins.Select(login => new
            {
                role = login.Role,
                name = login.Name,
                password = login.Password,
                principal_id = login.PrincipalId,
            })));
            await list.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var create = new NpgsqlCommand(LoginSql, connection, transaction))
            await create.ExecuteNonQueryAsync(cancellationToken);

        // Checked before the commit, so a login that could switch row-level security off is never left granted.
        foreach (var login in logins)
        {
            var violations = await DatabaseLoginGuard.FindViolationsAsync(connection, login.Name, cancellationToken);
            if (violations.Count > 0)
                throw DatabaseLoginGuard.Refusal(login.Name, violations);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
