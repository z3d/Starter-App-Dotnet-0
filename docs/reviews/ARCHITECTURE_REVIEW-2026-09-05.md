# Review — 2026-09-05

Reviewed commit `05179fc` for bugs and improvements. This is a targeted review with parallel
subsystem inspection and local verification, not an exhaustive audit. Reviewer sessions ended
before their final reports; the primary reviewer independently verified every finding below.
No runtime code was changed. The historical numerical score is not recalibrated by this review.

## Open findings

### 1. Invalid JSON bypasses sensitive-property masking
**Severity: High** | Files: `src/StarterApp.ServiceDefaults/Payloads/JsonPayloadRedactor.cs:38`, `src/StarterApp.ServiceDefaults/Payloads/PayloadCaptureSink.cs:140`

A JSON parse failure falls back to email-only text masking. For example,
`{"password":"review-sentinel-secret",` returns unchanged, and the sink puts that result in
its Information log. This also affects valid HTTP JSON cut off by `MaxPayloadBytes`, because
capture truncates before redaction. Capture runs before request validation, so rejected bodies
can reach this path. The archive is intentionally full-fidelity; the defect is exposure through
the operational log channel, which the recorded decision requires to be redacted.

**Fix**: Suppress the log payload when JSON cannot be parsed, while retaining the original in the
archive. Add malformed-JSON and capture-truncation regressions that assert a configured sensitive
value is absent from rendered operational logs. This is distinct from the accepted limitation
that arbitrary free text cannot be exhaustively classified as PII.

**Verification**: Calling the compiled redactor with the example above returned the sentinel
unchanged. Traced the bounded HTTP reader, redactor fallback, and sink log call. The fallback
returns normally, so the sink's redaction-exception suppression does not apply.

### 2. Entity indexing descends into sensitive objects
**Severity: High** | Files: `src/StarterApp.ServiceDefaults/Payloads/PayloadEntityReferenceExtractor.cs:114`

The extractor screens each scalar property's name but recursively visits its value regardless of
whether an ancestor is sensitive. For `{"password":{"customerId":"review-sentinel-secret"}}`,
the payload redactor masks the whole password object while the extractor emits the sentinel as a
customer ID. That value then appears in an entity-index blob path and the sink's Information log.
Caller-controlled bodies are captured even when the endpoint rejects their shape.

**Fix**: Skip the entire property subtree when its name matches the sensitive-name rules, including
arrays. Verify both extraction and rendered sink logs with nested objects/arrays; keep an ordinary
customerId positive control. This is a new ancestor-screening gap, separate from the previously
fixed scalar-name and route-path gaps.

**Verification**: The compiled extractor and blob-name builder produced
`entity-index/customer/review-sentinel-secret/.../review.jsonl` from the example, while the compiled
redactor produced `{"password":"***REDACTED***"}`.

### 3. Tombstone check and cache publication still race
**Severity: Medium** | Files: `src/StarterApp.Api/Infrastructure/Caching/CachingBehavior.cs:114`, `src/StarterApp.Api/Infrastructure/Caching/CachingBehavior.cs:138`

A reader can observe an absent tombstone, then pause while a writer commits and completes both
cache removal and tombstone creation. The reader subsequently publishes its old result without
another check; cache hits do not inspect the tombstone. A product can therefore remain stale for
its ten-minute TTL (or until a successful refresh). The existing tombstone-present tests cover
only the opposite interleaving. Stock reservations still check the live database; this is stale
read-model behavior, not proof of overselling.

**Fix**: Make publication conditional on an unchanged invalidation generation atomically in the
cache backend, or adopt a verified protocol that detects and removes a stale publication after
invalidation. Swapping the two invalidator calls or adding only another pre-write check is
insufficient. Add a deterministic interleaving regression before selecting the protocol.

**Verification**: A standalone harness linked the actual cache behavior and invalidator sources.
It paused the first null tombstone reply, completed invalidation, then resumed publication. The
next read returned `old database value` without invoking the database delegate. This new
interleaving evidence reopens the archive's previously resolved U7 race.

### 4. Null order-list elements throw instead of validating
**Severity: Medium** | Files: `src/StarterApp.Api/Application/Validators/CreateOrderCommandValidator.cs:27`, `src/StarterApp.Api/Application/Validators/CreateOrderCommandValidator.cs:37`

`{"customerId":1,"items":[null]}` deserializes successfully, but validation dereferences the null
item. A caller that passes the endpoint's identity/scope/MFA gates receives a 500 through the
unmapped NullReferenceException instead of a structured 400 validation response. The duplicate
product grouping dereferences the same elements, so guarding only the first loop is insufficient.

**Fix**: Return an indexed validation error for null elements and exclude them from duplicate
checks. Add a validator regression and an authenticated API regression for null-only and mixed
item lists.

**Verification**: Deserialized the literal with `JsonSerializerOptions.Web`, then enumerated the
compiled validator; deserialization accepted the null and validation threw NullReferenceException.
Traced mediator validator enumeration and the global exception-status mapping. No HTTP integration
reproduction was possible without Docker.

### 5. Pure validator tests unnecessarily require PostgreSQL
**Severity: Low** | Files: `src/StarterApp.Tests/Application/Commands/UpdateProductCommandHandlerTests.cs:5`, `src/StarterApp.Tests/Application/Commands/UpdateProductCommandHandlerTests.cs:14`

Pure validator tests share an Integration Tests collection and PostgresCommandHandlerTestBase
with database handler tests. Consequently even currency-code validation cannot run when Docker
is absent; the test fails during fixture initialization before exercising its validator.

**Fix**: Move pure validator and DTO/property tests into classes without database fixtures. Keep
handler persistence tests on PostgreSQL. This does not change the documented requirement that
the complete fast suite includes database-backed handler tests.

**Verification**: The fast-suite run failed these validator cases with DockerUnavailableException,
while inspection shows their bodies only construct commands and enumerate validators.

## Summary and fix order

| Priority | Finding | Severity |
|---|---|---|
| 1 | Invalid/truncated JSON leaks sensitive log values | High |
| 2 | Sensitive ancestors bypass entity-index exclusion | High |
| 3 | Null order elements produce 500 | Medium |
| 4 | Stale cache publication after invalidation | Medium |
| 5 | Isolate pure tests from PostgreSQL fixtures | Low |

The highest-value changes are closing the two operational-log masking gaps, followed by input
validation and the cache race. Existing conventions and guards refuted several broader concerns;
no dependency additions or architectural rewrites are justified by these findings.

## Verification and limits

- `dotnet format --verify-no-changes --no-restore`: passed.
- `dotnet build`: passed, zero warnings/errors, from a fresh isolated worktree at the reviewed commit.
- Convention-only test run: all 107 passed after updating the review records.
- `dotnet test --no-build --filter "FullyQualifiedName!~Integration&Category!=Aspire"`:
  584 passed, 56 failed; all 56 failures report DockerUnavailableException in database fixtures.
  These environment failures are not asserted to be application regressions.
- Standalone cache interleaving harness linked current source; domain/validation/payload harness
  referenced DLLs from the freshly built review worktree. The harnesses reproduced the four
  runtime findings without PostgreSQL, Redis, Azure, or HTTP integration infrastructure.
- Full HTTP and Aspire integration suites were not run. The linked-source cache double establishes
  the algorithmic race; it is not a Redis load test. No fixes or permanent regression tests landed.
- Extreme aggregate quantity overflow was reproduced (two lines with quantities int.MaxValue and
  1 pass validation and monetary guards but overflow OrderCreatedDomainEvent.TotalQuantity).
  The June archive already acknowledges extreme quantity overflow, so it is recorded here as an
  existing limitation, not counted as a new finding.
- Functions retry configuration and DAST harness candidates were not independently verified and
  are not reported as findings. No external dependency behavior is claimed from those candidates.
