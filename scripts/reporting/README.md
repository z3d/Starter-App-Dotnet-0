# Operational Reporting Queries

Versioned, reviewed support/ops queries — the sanctioned place to add SQL that answers
"what is the system doing", so every incident does not reinvent it ad hoc. Companion to
`docs/investigations/` (which records *verified diagnosis* queries per failure pattern)
and `docs/runbooks/event-replay.md`.

Rules:

- **Read-only.** Nothing here mutates state; recovery actions live in the runbooks and the
  sanctioned tooling (e.g. the DbMigrator `replay-outbox` verb).
- **Owner-scope aware.** Resource tables are multi-tenant; queries that aggregate across
  owners say so explicitly in their header comment.
- **Indexed access.** Filter on indexed columns (see the index definitions in
  `src/StarterApp.DbMigrator/Scripts/`); a support query must never become the load problem.
- Parameter placeholders use psql `:'name'` / `:name` syntax where a filter is expected.

| File | Question it answers |
|------|---------------------|
| `outbox-health.sql` | Backlog, errored rows, replay trail, daily publish volume |
| `job-run-history.sql` | What background jobs did recently (per run, with outcomes) |
| `orders-by-status-over-time.sql` | Order flow per status per day |
| `owner-scope-distribution.sql` | Rows per tenant/owner per resource table |
