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
- Functions retry configuration and DAST harness candidates were unverified at the end of the
  initial pass. The continuation below completes that work.


## Continuation — Functions retry and DAST verification

The user requested completion of the two unfinished review areas. This continuation reviewed
`87f2ff5` (runtime source unchanged from the initial pass) and adds three confirmed findings.
The current total is eight: three High, four Medium, and one Low. Runtime fixes remain open.

### 6. Service Bus subscribers do not receive the configured execution backoff
**Severity: High** | Files: `src/StarterApp.Functions/host.json:20`, `src/StarterApp.Functions/MessageSettlement.cs:63`, `src/StarterApp.Functions/MessageSettlement.cs:89`

The code relies on a runtime RetryContext to rethrow transient handler failures and get five
in-process retries, but Service Bus is not a supported trigger for Functions runtime execution
retry policies. The root `retry` object therefore does not provide the promised backoff for these
subscribers; with no retry context, settlement immediately abandons a transient failure.
AppHost runs capture FailClosed and configures MaxDeliveryCount=5, so an archive outage can consume
the delivery budget and dead-letter otherwise valid events without the intended waiting window.
The messages are recoverable from the dead-letter queue; this is not silent deletion.

**Fix**: Implement an explicit bounded, cancellation-aware retry policy around retryable handler
work before settlement, with an execution/deadline budget inside lock renewal. Alternatively use
a deliberately designed broker redelivery strategy that actually provides the required delay.
Do not retry successful handler work solely because completion failed. Remove the ineffective
configuration and replace the config-presence convention with behavior tests and a real
Service Bus transient-failure integration test. `clientRetryOptions` alone is not a fix: it
retries broker interactions, not function executions.

**Verification**: Microsoft's [retry guidance](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-error-pages#retry-policies)
lists the supported execution-retry triggers without Service Bus. Its
[Service Bus host settings](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-service-bus#hostjson-settings)
distinguish broker-client retries from execution retries. Generated `functions.metadata` has no
retry definition for either subscriber. All eight MessageSettlementTests pass, including the
null-context test that immediately abandons; the two FunctionsHostConfigConventionTests also pass,
because they inspect JSON shape and timing arithmetic rather than actual trigger behavior.
The supported-trigger conclusion comes from current official documentation and source/config
inspection; no live Azure or emulator execution was performed. This reopens the earlier
host-backoff closure and its subsequent lock-window hardening: the intended timing policy was
never established for Service Bus by those tests.

### 7. DAST ignores the requested target's scheme, host, and base path
**Severity: Medium** | Files: `dast/run-dast.sh:26`, `dast/run-dast.sh:188`, `dast/automation.yaml:19`, `dast/automation.yaml:85`

TARGET_URL supplies only a numeric port to the rendered ZAP plan. Every scanner URL still uses
`http://host.docker.internal`, even in SKIP_BOOT mode, and any base path is discarded. For example,
`https://review.invalid:8443/custompath` becomes `http://host.docker.internal:8443`. A TLS target
fails to scan, or an unrelated local service on that port is scanned while the requested target
is untouched. A token supplied for the intended target is also attached to the substituted target.

**Fix**: Render one validated scanner base URL, preserving scheme, remote hostname, port, and base
path. Translate loopback hosts only when needed for the container boundary. Build context,
OpenAPI, target, and filter URLs consistently, escaping YAML and regex fields appropriately.
If only local HTTP root targets are intended, reject unsupported URLs explicitly and document
that constraint instead of silently substituting a different destination.

**Verification**: Ran an unmodified copy of the runner/template with SKIP_BOOT=1, a dummy token,
the example URL, and a container-CLI stub that emits synthetic ZAP report artifacts. The runner
announced the supplied HTTPS URL but rendered all scanner URLs as local HTTP on port 8443.
No container, scan, or network connection occurred; this proves plan generation, not vulnerability
coverage. The normal default local-HTTP run is unaffected.

### 8. Cross-owner list probe accepts HTTP errors as empty results
**Severity: Medium** | Files: `dast/run-dast.sh:327`, `dast/run-dast.sh:364`

The orders-by-customer probe captures only the body and uses `(.data // []) | length`, without
checking the HTTP status or requiring a data array. An HTTP 500 ProblemDetails body is therefore
reported as an owner-filtered empty list, even though the endpoint never produced a successful
list response. The other by-id/delete probes check status, but they do not establish this route's
behavior and cannot refute a route-specific failure.

**Fix**: Capture status and body together; require HTTP 200 and a correctly shaped array at `data`
with length zero. Test 401, 403, 429, and 500 ProblemDetails bodies, a missing data property, and a
non-array data property as failures; keep a 200 response with `data: []` as the positive control.

**Verification**: Executed the exact list-probe block from the script with xo_body returning the
body of a simulated HTTP 500 response:
`{"type":"about:blank","title":"Internal Server Error","status":500}`.
It printed `OK orders/customer/900001 -> empty list (owner-filtered)` and left `idor_fail=0`.
This proves a false pass for this probe, not that the complete scan always passes during an outage.

### Continuation summary

| Fix order within this continuation | Finding | Severity |
|---|---|---|
| 1 | Missing Service Bus execution backoff | High |
| 2 | DAST target substitution | Medium |
| 3 | List-probe false pass on HTTP errors | Medium |

The two initial log-masking findings remain high-priority alongside the retry defect. Complete
runtime fixes with failure-path regressions, then correct the scan harness so its results describe
the intended target and successful endpoint responses. No new library is necessary to establish
these findings.

Continuation validation: formatting verification and build passed with zero warnings/errors;
107 API conventions, eight settlement tests, and two Functions host-config conventions passed.
Shell reproductions used only temporary copies and synthetic inputs. Full container-backed
Functions/ZAP validation remains unavailable without Docker. This continuation modifies only
review documentation, not runtime or test behavior.
