# Review — 2026-09-06 (fix review)

Reviewed commit `b1a71ff` ("fix: close eight runtime and DAST review findings"), the commit that
resolved the [2026-09-05 review](ARCHITECTURE_REVIEW-2026-09-05.md), before accepting it on
`main`. Two reviewers ran in parallel over the full diff: the repo's backend architect
(rule adherence against `CLAUDE.md` and the skills, plus a bug hunt) and its security auditor
(OWASP mapped to this stack, plus the repo's own threat model). The primary reviewer
spot-checked their strongest claims by timing the test suite and re-reading the settlement and
caching code line by line.

Verdict: the commit stays. Every one of the eight findings is closed by a real code change with
a failing-before regression, no `CLAUDE.md` rule is violated, and CI ran the build, integration,
and Aspire jobs green on the commit. What follows are residuals of those fixes, not defects in the
decision to land them.

## Verified clean

- **Cache generation protocol.** The invalidator writes a fresh generation before removing the
  value; the behavior pins expiry before any I/O and rejects any envelope whose generation
  differs. Every interleaving walked (writer between the pre-write check and `SetAsync`, slow
  publisher, legacy `"1"` tombstone, pre-envelope value) fails safe. Generations derive from the
  owner-scoped key, so they are per owner.
- **Subscriber retry.** Service Bus triggers never honoured the `host.json` execution retry
  policy, so the old code went straight to Abandon on the first transient failure; the record's
  claim that two earlier closures had tested configuration shape rather than behaviour is right.
  The new loop is bounded, dead-letters poison immediately, abandons exactly once on exhaustion,
  and keeps `Complete` outside the loop.
- **Redaction and indexing.** `Redact` is consumed only by the sink's log path, so the
  suppression marker cannot reach the archive. The extractor's subtree skip covers objects and
  arrays under a sensitive name.
- **DAST.** Every caller-controlled value reaches `jq` via `--arg`; the target URL is validated
  against an anchored regex with a hard failure on rejection; the cross-owner probe requires
  literal status codes and a well-formed empty `data` array; the token lands only in the
  git-ignored rendered plan and the `Authorization` header, never in an uploaded artifact.
- **Coverage.** The eighteen pure validator and property tests moved one-to-one out of the seven
  PostgreSQL fixture classes; no method was dropped.

## Findings

| # | Finding | Severity | Outcome |
|---|---|---|---|
| 1 | Three settlement tests call the public overload and sleep on real backoff; measured 16 tests in 10 s | Medium | Fixed |
| 2 | `MaxRetries` and `ExecutionTimeout` are independent constants; backoff already consumed half the deadline and nothing asserted the split | Medium | Fixed |
| 3 | Under `FailClosed`, an entity-index failure after the archive append rethrows and the in-process retry appends the same archive line again, up to six per delivery | Medium | Recorded, not fixed |
| 4 | The entity-reference extractor never screens the *resolved* entity type, so `POST /api/v1/passwords` with a bare `id` indexes the value under `password` | Medium | Fixed |
| 5 | Nothing enforced `ICacheable ⇒ IOwnerScopedRequest`, and an owner-scoped request without an identity fell back to a global key | Medium | Fixed |
| 6 | `token.ThrowIfCancellationRequested()` ran before dead-lettering and settlement used the deadline-linked token, so poison surfacing near the deadline was redelivered instead | Low | Fixed |
| 7 | The replacement host-config convention asserted exactly on its own ceiling (4:00 ≤ 4:00), with none of the headroom the test it replaced demanded | Low | Fixed |
| 8 | The invalidator's new order runs the generation write first inside one `try`; if that write fails the removal is skipped and the stale value stays servable for its whole duration | Low | Fixed |
| 9 | `JsonNode.Parse` reports duplicate property names as `ArgumentException`, which the redactor's `catch (JsonException)` misses; only the sink's broad catch hid it | Low | Fixed |
| 10 | The DAST renderer turns an unknown `__ZAP_*__` placeholder into YAML `null`, which would widen an alert filter or drop a context URL silently | Low | Fixed |
| 11 | The DAST shell stub returns the expected code for probes 1, 2, 3, 5 and 6, so only probe 4 could ever fail in the harness | Low | Fixed |
| 12 | `CreateOrderCommandHandler` still dereferences null items in its duplicate guard; unreachable via the mediator, reachable from tests that construct the handler | Low | Fixed |
| 13 | A generation mismatch logged as "not a valid envelope", reading routine invalidation as corruption | Low | Fixed |
| 14 | Each of the 16 concurrent invocations can now hold its slot for the whole deadline during an outage; the throughput consequence was unrecorded | Low | Recorded |

## Resolution — 2026-09-06

| # | Change and regression evidence |
|---|---|
| 1 | Both subscribers take the host `TimeProvider` and `MessageSettlement` waits through it. `PayloadFunctionTests` supplies a provider whose timers fire at once and records the waits; the settlement tests use the injectable-delay overload throughout. The fast suite dropped from 10 s to 3 s. |
| 2 | `BackoffFor`, `TotalBackoff` and `MinimumHandlerBudgetPerAttempt` are exposed; a test asserts total backoff (120 s) plus six attempts of at least 15 s fits inside the deadline. The deadline is now 210 s. Raising `MaxRetries` or the schedule without widening the deadline fails that test. |
| 3 | Not changed. Making the entity-index appends best-effort *is* the per-stage capture-sink isolation that is deferred with a trigger in the living review; that trigger has not been hit. The consequence (at-least-once duplication of archive rows, never loss) is now stated on the deferred entry as a second trigger. |
| 4 | `AddReference`, the choke point every source funnels through, screens the normalized entity type. Three cases (route-derived type, metadata path, and `nationalIds`) assert no reference is emitted; the sensitive-route case still passes. |
| 5 | `CachingConventionTests.CacheableQueries_MustBeOwnerScoped` fails on any `ICacheable` that omits the marker. `CachingBehavior` serves an owner-scoped request without an identity uncached rather than under a bare key; a test verifies the cache is neither read nor written. The expired-envelope race test now uses a real owner key. |
| 6 | Dead-letter, abandon and complete run on the host token. Two tests drive a 50 ms deadline against a 300 ms handler: a late `JsonException` is dead-lettered, and a late success is completed. Host shutdown still leaves the message unsettled. |
| 7 | `SettlementReserve` (30 s) is a named constant. The convention asserts `ExecutionTimeout + SettlementReserve < maxAutoLockRenewalDuration` strictly (4:00 < 5:00) and `ExecutionTimeout ≤ 80 %` of the window (3:30 ≤ 4:00), each with real margin. |
| 8 | The generation write and the removal sit in separate `try` blocks; a failed generation write logs and still evicts. A test throws from `SetAsync` and verifies `RemoveAsync` runs once. |
| 9 | The redactor catches `JsonException or ArgumentException`. A test with duplicate keys asserts the exact suppression marker, which also proves the exception is real on this runtime. |
| 10 | `dast_render_plan` raises a `jq` error naming the placeholder; a harness case renders a template with `__ZAP_TYPO_URL__` and requires failure with no `null` in the output. |
| 11 | The `curl` stub reads `PROBE_BYID_CODE` and `PROBE_DELETE_CODE`; six leak cases (200/403/500 by id, 204/404/500 delete) must fail the probe. The harness now runs 39 cases. |
| 12 | `EnsureNoDuplicateProducts` throws `ValidationException` on a null element before grouping, mirroring the validator. The regression sits in the PostgreSQL-backed handler class. |
| 13 | The miss log distinguishes an unreadable envelope from a superseded or expired one. |
| 14 | `docs/DECISIONS.md` now states the backpressure consequence beside the retry timing. |

### Validation

`dotnet format --verify-no-changes` clean; `dotnet build` 0 warnings, 0 errors. The full
`StarterApp.Tests` project: 771 passed, 0 failed, through the Podman machine's Docker-compatible
socket (the machine was stopped at first, which is what the earlier `DockerUnavailable` failures
on this box were; `podman machine start` is the whole fix). `StarterApp.AppHost.Tests`
non-Aspire: 14 passed. DAST harness: 39 passed. The nightly DAST scan has not run against the
rewritten renderer yet; trigger it with `gh workflow run dast.yml` rather than waiting for the
schedule.

The score stays at 8.1. These are residuals of a verified fix, found by a review that the fix
itself invited, and none reopens a closed finding.

## Continuation — emulator assessment

Prompted by the question whether a Service Bus emulator library should replace or supplement
the fake-based settlement tests. Both candidates exist: `Testcontainers.ServiceBus` (4.14.0,
about two million downloads) wraps Microsoft's emulator image and its MSSQL sidecar for xunit
use without Aspire; `Spotflow.InMemory.Azure.ServiceBus` (0.16.4) fakes the SDK client, sender,
receiver and processor in-process.

What the emulator proves here today: one Aspire fact publishes an order and observes both
subscribers' archive blobs, so publish, consume and capture are covered end to end. Nothing
observes a settlement outcome; `SubQueue.DeadLetter` and `CreateReceiver` appear nowhere in the
repo, and `StarterApp.AppHost.Tests` does not reference `Azure.Messaging.ServiceBus`.

| Behaviour | Proven by | A broker would add |
|---|---|---|
| Retry then complete | `MessageSettlementTests` against the recording fake | Complete succeeds on a still-valid lock; no reappearance |
| Backoff then abandon | Same | Abandon redelivers, `DeliveryCount` increments, fifth delivery dead-letters |
| Dead-letter reason and description | Same, including the 2048-character truncation | The broker accepts the fields; the ceiling has never been exercised |
| Lock renewal across the 210 s deadline | `FunctionsHostConfigConventionTests` arithmetic only | That `maxAutoLockRenewalDuration` renews a 30 s lock across the whole handler |

Decision: no library. The Aspire fixture already hands any fact a live emulator connection
string, so `Testcontainers.ServiceBus` adds container cost without fidelity; an in-memory fake
cannot model the Functions host or the broker, which is exactly where the unproven claims live.
A broker-timing test would wait roughly 3.5 minutes inside the `aspire` job that is already a
cold-start flake watch-item with no `timeout-minutes`, and a dead-letter test cannot be triggered
because no subscriber deserializes yet, so nothing throws what `IsNonRetryable` dead-letters on.
The re-add trigger is recorded in the living review's deferred list.

### 15. Outbox end-to-end fact relied on test ordering for the Functions gate

**Severity: Low** | File: `src/StarterApp.AppHost.Tests/OutboxToServiceBusIntegrationTests.cs`

`CreateOrder_ShouldWriteAndProcessOutboxEvent` asserted subscriber-produced archive blobs
within a 60-second poll but never awaited `EnsureFunctionsReadyAsync()`, the gate the fixture
comment says every subscriber-dependent fact must use. It passed only when
`FunctionsContainerIntegrationTests` had already paid the in-container image build and boot,
which xunit does not guarantee. This is a plausible cause of the recorded `aspire` flake.

**Resolution:** the fact awaits the gate immediately before the subscriber blob poll. The
publish-side assertions stay independent of the Functions boot, and the fixture caches the gate,
so the cost is paid once per collection. Verified by the `aspire` CI job on the commit.
