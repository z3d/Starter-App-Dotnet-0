# Fresh-eyes whole-solution review, 2026-09-27

Scope: everything under `src/` and `dev/`, CI workflows, and Dockerfiles, reviewed as a whole
rather than as a diff. The reviewers were deliberately kept away from earlier review records,
`docs/ARCHITECTURE_REVIEW.md`, and `docs/investigations/`, so that they would judge the code
independently. There were three passes: a manual read, the `backend-architect` subagent, and the
`security-auditor` subagent. Every finding below was re-checked against the code, and cross-checked
against earlier records only after the review was done.

## Verdict

The engineering is sound but out of proportion. The hard paths hold:

- order creation, with one stable Id across retries and atomic stock reservation;
- the outbox claim (SKIP LOCKED, the `ProcessingId` token, MessageId-based duplicate detection);
- JWT validation;
- owner scoping at the predicate, the policy, and the write guard;
- supply-chain pinning.

The weight sits in the wrong place. There are about 9.4k lines of `src/`, against about 6k lines of
convention and consistency machinery and about 29k lines of Markdown. `CachingBehavior` spends 172
lines on generation tombstones and refresh-ahead to guard two by-id lookups. Payload capture, at
about 1.5k lines, is the heaviest subsystem and also held the worst performance defect. Meanwhile
the plain exemplars an adopter reaches for first were missing or wrong: outbox throughput,
request idempotency, consumer dedup, and a Customer concurrency token.

The "a seam is an exemplar" rule is sound for small interfaces. It should not be read as licence to
add more infrastructure while those exemplars are missing.

## Fixed the same day

| # | Finding | Fix |
|---|---|---|
| 1 | High. `OutboxProcessor` slept `PollingIntervalSeconds` after every batch, even a full one. That capped publishing at `BatchSize` per interval, 4 events/s per replica at the defaults, so any sustained load above it grew the backlog without bound. | `762cba1`. `ProcessBatchAsync` reports a backlog only when a full batch published cleanly, and the loop then skips the sleep. Batches with retries or pauses still wait, because retried rows are reclaimable at once and would otherwise spin. Test: `ProcessBatch_ReportsBacklogOnlyWhenFullBatchPublishedCleanly`. |
| 2 | High. `AzureBlobPayloadArchiveStore` called `CreateIfNotExists` on the container and on the blob before every `AppendBlock`. That is three serial round trips per append, and about twelve per captured HTTP request. | `762cba1`. Appends go through directly; the container or blob is created only on `ContainerNotFound`/`BlobNotFound`. Test: `AzureBlobAppendTests` covers the existing, missing-blob, missing-container, and other-error paths. |
| 3 | Medium. `Customer` was the only aggregate root without a row-version token, so concurrent updates were last-write-wins. | `b3167c3`. `Customer` maps `xmin`, `ConcurrencyCriticalEntities_MustUseRowVersionTokens` requires it, and `ConcurrentCustomerWrites_StaleUpdate_ThrowsConcurrencyException` runs against PostgreSQL. |
| 4 | Medium. The published build ships the dev realm, which hardcodes `amr: ["pwd","mfa"]` and has well-known users, under `start-dev`. Only the operator IP allow-list stood between a derived project and shipping it as a real IdP. | `b3167c3`. Publish mode throws unless `DEPLOY_ACK_DEV_IDP=true`, mirroring the dev-tunnel gate. `docs/GETTING-STARTED.md` documents it. |

## Tried and reverted

| # | Finding | Outcome |
|---|---|---|
| 5 | "Unauthenticated traffic is never rate-limited: `UseAuthorization` runs before `UseRateLimiter`." Raised independently by both subagents. | `762cba1` moved the limiter between identity and authorization; `f73421c` reverted it. This is the review playbook's known false positive: inbound volume belongs to the upstream gateway. The revert also has a concrete reason. Without forwarded headers (see the [2026-09-12 record](SECURITY-REVIEW-2026-09-12-identity.md) #7), every anonymous caller shares one bucket, so a flood turns a legitimate client's expired-token 401 into a 429 that it does not refresh on. |

## Open

| # | Sev | Finding | Trigger / fix |
|---|---|---|---|
| 6 | Medium | Under FailClosed, a capture failure pauses the outbox without spending retry budget, and claims are strictly ordered by `occurred_on_utc`. A capture that always fails for one message, or a store-wide 403 from a role-assignment mistake, blocks all publishing indefinitely, and the only signal is a repeated warning. | Route non-transient capture failures (`PayloadCaptureFailureClassifier`) through the retry and error path, and alert on consecutive pauses. |
| 7 | Medium | `POST /api/v1/orders` has no request idempotency. A client retry after a timeout creates a second order and reserves stock twice. The server-side retry is already safe. | An `Idempotency-Key` exemplar. The earlier runtime-hardening record named a caller-supplied key as the long-term answer for creates. |
| 8 | Medium | No consumer shows dedup. Delivery is at-least-once and unordered, and both Functions subscribers are TODO stubs, so adopters will write handlers that are not idempotent. | An inbox / processed-MessageId exemplar with the first real subscriber, which is the same trigger as the broker-observed settlement tests. |
| 9 | Low | Inbound capture runs before auth, so an anonymous body such as `{"customerId":42}` writes `entity-index/customer/42/...` next to real entries, and inbound records carry no subject. | Tag index entries with the auth outcome, or index only authenticated requests at response time. |
| 10 | Low | The deep probes (`/health`, `/healthiness`) are anonymous, exempt from rate limiting, and reachable through the ingress. Each hit touches the DB, Redis, Service Bus, and blob storage. | Serve them internally only, or cache the result for a few seconds. |
| 11 | Low | `text/plain` bodies reach logs with only email masking. | Log size and hash for non-JSON, as for invalid JSON. |
| 12 | Low | The Functions image has no `USER` line and runs as root; the API and migrator images drop to `$APP_UID`. | Add `USER $APP_UID`. |
| 13 | Low | The migrator takes no advisory lock, so overlapping deploy jobs can collide. | `pg_advisory_lock` around the DbUp run. |
| 14 | Low | `InternalsVisibleTo("StarterApp.Api")` lets production code reach the test-only `Order.Reconstitute`, and no convention bars it. | Add a convention, or make the v7-id `Order` constructor public and drop the grant. |
| 15 | Low | `.gitleaks.toml` still allowlists a string for the deleted `StarterApp.Gateway`; the README project tree still lists the test projects under `src/`. | Housekeeping. |
| 16 | Note | Products are owner-scoped, so a user can order only products they created. There is no shared catalog, which surprises anyone modelling a store. | A deliberate demonstration of owner scoping; state it in the README. |

Previously recorded and not re-raised: `ValidTypes`, forwarded headers, and cross-owner id
enumeration (2026-09-12 #10, #7, #8); the page cap of 100,000 (2026-06 archive U4, an overflow
guard; deep OFFSET cost is its accepted consequence).

## Solid, verified

- **Order creation.** One stable Id minted before the retry loop and re-checked on each attempt,
  with atomic `stock >= qty` reservation in the same transaction.
- **The outbox.** The SKIP LOCKED claim, the detach-and-save-the-rest path for stolen rows, and
  MessageId = outbox Id with duplicate detection.
- **Identity.**
  - A single `AddJwtBearer` with pinned algorithms.
  - `sub` and `tid` are both required.
  - No reads of `HttpContext.User` outside `Infrastructure/Identity`.
- **Owner scoping.**
  - All Dapper reads filter by owner and tenant.
  - `OwnerAuthorizationWriteGuard` blocks writes, including `ExecuteUpdate`.
  - Cache keys hash tenant and subject.
- **CI and images.** SHA-pinned actions, least-privilege default permissions, checksum-verified
  binaries, and digest-pinned base images.

## Follow-up, 2026-09-27

Worked the open list the same day, in the order the maintainer asked. Numbers are this record's.

| # | Outcome |
|---|---|
| 6 | **Fixed differently from the proposal**, `cd66b49`. Routing non-transient capture failures through the retry budget would have reversed the recorded pause-never-poison rule (#79). Instead a store failure (any `RequestFailedException`, or a transient one) still pauses the batch, now at Error when not transient; any other failure holds only that message until its lock expires and the rest of the batch publishes. |
| 12 | **Fixed**, `955ecf8`. The base image already gives `$APP_UID` `/home` and a writable `Secrets`; the only blocker was port 80. The Functions image runs as uid 1654 on 8080 (booted and served the host page as that user), and a convention requires `USER` in every shipping Dockerfile's final stage. |
| 13 | **Fixed**, `e10e1a6`. The migrator holds a session `pg_advisory_lock` around the DbUp upgrade; `MigrationLockTests` shows a second run waits, then succeeds. |
| 15 | **Half false**, `b12577d`. The README tree and Quick Start path are fixed. The Gateway gitleaks entry is not stale: the key is in history (`b1bf37f..7ffe7ed`) and the secret scan covers full history, so it stays with a comment saying why. |
| 7 | **Fixed**, `e97988b`. Optional `Idempotency-Key` on `POST /api/v1/orders`, backed by `idempotency_records`; replay, 422 on a changed body, and a concurrent same-key race are integration-tested. |
| 8 | **Fixed**, `32e7362`. `IMessageInbox` claims `(consumer, MessageId)` and runs the work in one transaction; both subscribers use it and a convention requires it of every `[ServiceBusTrigger]` function. |
| 9 | **Fixed**, `31f8f8a`. Capture stays first; the inbound entity-index lines are deferred and written only for an authenticated caller. |
| 10 | **Accepted by design.** The deep probes are meant to sit behind the upstream gateway, which keeps them off the public surface; recorded in `DECISIONS.md` beside the anonymous-probe rule. |
| 14 | **Fixed.** `ProductionCode_MustNotCallReconstitute` scans every production assembly's IL for a call to a Domain `Reconstitute`, and proves it is not vacuous by finding the test assembly's calls. The grant stays: the API legitimately uses the Order constructor, `RecordCreation` and the event-stamping internals. |
| 16 | **Fixed.** The README says the sample domain is owner-scoped end to end, with no shared catalogue, by design. |
| 11 | Still open. |

Also back-ported from agentdesk, `a861d0a`: the xmin convention discovers every mutable domain entity instead of keeping a hand list.
