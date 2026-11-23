# Deploying a build that changes who the database trusts

`docs/DECISIONS.md`, "How a derived project deploys", is the usual order: migrate, start the new revisions at no traffic, probe, shift traffic. That order assumes the build still serving can work against the new schema. One kind of change breaks the assumption, and this page is its order.

## When this page applies

A deployment is **not a rolling one** when its migrations do either of these:

- **Force a tenant policy on a table the serving build reads or writes** (a `Contract` script with `FORCE ROW LEVEL SECURITY`, as `0013_Contract_ForceTenantRowSecurity.sql` does). A build from before the tenant binding sets no `app.tenant_id`, so once the policy is forced it reads no rows and its inserts are refused (42501) until the new build starts.
- **Move the API or the worker to a login of their own** (`DatabaseLogins:Api`, `DatabaseLogins:Functions`). The logins exist only after the migrator has run, and a build that signs in as an administrator is refused at start-up by `DatabaseLoginGuard`.

A derived project meets both the first time it takes "Tenant isolation is held by the database". Any later deployment whose scripts only add (`Expand`) follows the usual order.

## The order

1. **Stop the serving build.** Scale the API and the worker to zero, or take traffic off and stop the worker's triggers. Requests fail for the length of the deployment; say so beforehand.
2. **Run the migrator as the server's administrator, with both logins named.** It runs the scripts, then `ApplicationRoleSetup` restates the grants and creates the two logins: `DatabaseLogins:Api` and `DatabaseLogins:Functions`, each with a `PrincipalId` (the identity's object id) on an Entra-only server, or a `Password` on a local one. Wait for it to succeed.
3. **Start the new build as those logins.** The API's connection string names the API's login and the worker's names the worker's. Neither is the administrator.
4. **Probe before traffic.** The API's `/health/ready` answers when the journal holds every script the build carries. The worker's `/api/health` answers 200 when it can read each of its subscriptions and reach the database.
5. **Shift traffic.**

## Going back

Moving traffic back to the build before does not work here: that build binds no tenant, or signs in as a login the guard now refuses. Going back is a deployment of its own: stop, restore the database from the backup taken before step 2 (or run a script that takes `FORCE` off, if the logins are the only thing to undo), start the old build. Take the backup first.

## What has not been proven

Against Azure Database for PostgreSQL: that the migrator's Entra administrator may call `pgaadauth_create_principal_with_oid` and grant the roles, and that a token for each identity signs in as the principal it makes. The first deployment checks both.

## Owner tools

A tool that signs in as the tables' owner (the migrator's `replay-outbox` verb, a support script) meets a forced policy like anyone else when the table it reads has a `tenant_id`. The template's outbox has no tenant column, so its replay is unaffected. A derived project that gives the outbox one takes `FORCE` off inside the replay's one transaction, and puts it back in the same transaction.
