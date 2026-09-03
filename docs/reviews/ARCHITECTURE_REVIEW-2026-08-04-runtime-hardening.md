# Architecture Review — Runtime Hardening

**Date:** 2026-08-04

**Snapshot:** `b181905` (`origin/main` after the post-IdP review record)

**Focus:** secret handling, retention correctness, retry idempotency, and cache availability

## Assessment

The main architectural controls remain sound: writes use EF Core with the transactional outbox,
reads use owner-scoped Dapper queries, identity is validated in the API, and the existing
claim/retry/replay defenses held under review. Five runtime findings survived direct code review
and independent refutation. One can disclose a database credential through routine logs; four
affect retention, replay recovery, concurrent-create semantics, or read availability.

The live architecture score was reduced from 7.7 to 7.1 until these five findings and their
regression tests land. The lower-confidence convention and documentation opportunities found in
the same pass were deliberately not promoted into the living review.

## Findings

### 1. Quoted database passwords can leak into logs

**Severity: High** | Files: `src/StarterApp.DbMigrator/Program.cs:82`,
`src/StarterApp.DbMigrator/DatabaseMigrationEngine.cs:43`, `src/StarterApp.Api/Program.cs:114`

All three password-masking helpers use a regular expression whose value match stops at the first
semicolon. PostgreSQL connection strings permit quoted password values containing semicolons, so
the helpers replace only the first segment and emit the remaining password suffix into routine
migrator output (and Development API logs). A synthetic valid quoted password reproduced the
leak; no real credential was used.

**Fix:** Parse the connection string with `NpgsqlConnectionStringBuilder`, remove or replace the
password, and centralize the sanitizer. Prefer logging only the host, port, and database rather
than reconstructing the full connection string. Add regression cases for `Password`/`Pwd`, quoted
semicolons, and malformed input without ever echoing the original secret on failure.

### 2. Errored outbox rows can lose their replay-retention window

**Severity: Medium** | Files: `src/StarterApp.Api/Infrastructure/Outbox/OutboxProcessor.cs:89`,
`src/StarterApp.Api/Infrastructure/Outbox/OutboxMessage.cs:43`

Processed rows are retained from `ProcessedOnUtc`, but errored rows are purged when their original
`OccurredOnUtc` crosses the cutoff because `MarkAsError` records no failure timestamp. An event
that remains pending through an outage longer than `RetentionDays` can therefore fail permanently
after recovery and be deleted by the same processor loop immediately after its outcome is saved,
leaving operators no replay window.

**Fix:** Add a migrated `ErroredOnUtc` column, set it when a message becomes permanently errored,
clear it in both replay paths, and base errored-row retention on that timestamp. Add a regression
where an old pending event errors now and remains replayable until a full retention period elapses.

### 3. Payload cleanup has a fixed throughput ceiling below modest traffic

**Severity: Medium** | Files:
`src/StarterApp.ServiceDefaults/Payloads/PayloadCaptureOptions.cs:61`,
`src/StarterApp.ServiceDefaults/Payloads/AzureBlobPayloadArchiveStore.cs:102`,
`src/StarterApp.Functions/Dockerfile:36`

Cleanup runs hourly and deletes at most `CleanupBatchSize` blobs per prefix: 500 by default and
10,000 at the validated maximum. Archive naming creates approximately one archive blob per
request correlation, so the default keeps pace only below 500 requests/hour (about 0.14 requests
per second), before entity-index fan-out. Sustained ingestion above the configured deletion rate
causes expired payloads and PII to accumulate indefinitely despite `RetentionDays`.

**Fix:** Drain successive bounded pages until the prefix is caught up or a documented execution
time budget is reached. Record the oldest expired blob/backlog size and alert when cleanup cannot
catch up; validate schedule and batch settings as a capacity pair if a hard per-run cap remains.

### 4. Concurrent customer creation can return another request's row

**Severity: Medium** | Files: `src/StarterApp.Api/Application/Commands/CreateCustomerCommand.cs:41`

The natural-key lookup inside the EF execution-strategy delegate runs on its first invocation as
well as on retries. Two requests can both pass the pre-check; after one commits, the other's first
delegate invocation can find that same-email row and return it as a successful create—even when
its requested name differs—instead of taking the unique-constraint path and returning `409`.

**Fix:** Distinguish the initial execution from an actual retry and accept the natural-key row only
on a retry whose stored representation matches the request; otherwise return a duplicate-email
conflict. A caller-supplied idempotency key or stable client-generated identifier is the stronger
long-term solution. Add a fault/race integration test against PostgreSQL.

### 5. Redis failures take healthy database-backed reads down

**Severity: Medium** | Files:
`src/StarterApp.Api/Infrastructure/Caching/CachingBehavior.cs:32`,
`src/StarterApp.Api/Infrastructure/Caching/CachingBehavior.cs:95`,
`src/StarterApp.Api/Infrastructure/Caching/CachingBehavior.cs:108`

The initial cache read, invalidation-tombstone read, and cache write all propagate non-cancellation
cache exceptions. Consequently, a Redis outage makes cacheable product/customer reads return
`500` before reaching PostgreSQL, or after PostgreSQL has already returned a valid result. This is
inconsistent with the recorded best-effort cache posture and the fail-open invalidation path.

**Fix:** Treat an initial cache-access failure as a miss and run the handler. After a successful
handler result, log and swallow tombstone/store failures; if the tombstone cannot be checked, skip
the write to avoid repopulating a potentially stale value. Preserve cancellation propagation and
add throwing-`IDistributedCache` tests for get, tombstone, and set paths.

## Summary

| Finding | Severity | Impact |
|---|---|---|
| Quoted-password masking leaks suffixes | High | Database credentials can reach routine logs |
| Errored outbox retention uses occurrence time | Medium | Old failures can be deleted with no replay window |
| Payload cleanup cannot catch sustained ingestion | Medium | Expired PII can outlive the configured retention period |
| Customer retry recheck accepts a concurrent winner | Medium | A create can return another request's representation |
| Cache infrastructure failures propagate | Medium | Redis outages take healthy database reads down |

## Fix Order

1. Replace connection-string regex masking because it crosses the secret boundary directly.
2. Correct errored-outbox retention so a permanent failure always receives an operator replay
   window.
3. Make payload cleanup drain or report its backlog so `RetentionDays` is an enforceable property.
4. Separate customer retry recovery from first-attempt duplicate races.
5. Make cache access fail open without weakening invalidation-tombstone safety.

## Verification Performed

- Re-read each path and its callers against `b181905`; the post-IdP commit changed documentation
  only, so the reviewed runtime code is unchanged from the independently checked snapshot.
- Reproduced the password-mask failure with a synthetic valid quoted connection string; no real
  secret was read or used.
- Focused domain/convention run — 253 tests passed.
- Focused cache/exception run — 20 tests passed.
- Filtered AppHost test run — 14 tests passed.
- The broader advertised fast filter built the solution, but database-backed command-handler
  tests could not start because no Docker/Podman endpoint was available. No assertion failure
  independent of that infrastructure error was observed.

## Resolution Standard

Each finding remains open until its regression test is proven red against the injected failure,
the regression is reverted, and the relevant suite passes. Update both this record and
`docs/ARCHITECTURE_REVIEW.md` when closing a finding; schema changes must also update the replay
SQL and convention coverage in the same change.

## Resolution (2026-09-03)

| # | Fix | Regression test |
|---|---|---|
| 1 | Regex masks replaced by `ConnectionStringDescriptor.Describe` (structural parse; host, port, database, user only; fixed placeholder on parse failure), linked into the migrator | `ConnectionStringDescriptorTests` |
| 2 | `ErroredOnUtc` column (`0006`), stamped by `MarkAsError`, cleared by both replay paths, retention counted from it | `OutboxProcessorTests` cleanup fact, `OutboxMessageTests`, `OutboxReplayTests` |
| 3 | One listing pass per prefix deleting every expired blob, with `CleanupTimeBudgetSeconds` (default 300, inside every plan's default `functionTimeout`) split across prefixes and checked inline; `BudgetExhausted` → warning + `Degraded` job run. Validation pass replaced a per-page drain that re-listed the non-chronological `entity-index/` prefix quadratically | `InMemoryPayloadArchiveStoreTests` |
| 4 | Natural-key recovery only on a retry, and only when the stored name matches; first-attempt races reach the unique constraint | `CreateCustomerCommandHandlerTests` race fact against PostgreSQL |
| 5 | Cache read failure → miss; tombstone check failure → skip write; write failure → logged; cancellation propagates | `CachingBehaviorTests` (four facts) |
