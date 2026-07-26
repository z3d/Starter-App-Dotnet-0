# Review Playbook

## Analysis bias list

Ordered by how often each has actually produced findings here:

1. **Exception → HTTP status, end-to-end.** Trace every exception type from throw site through the middleware to its status code. The #1 source of bugs in .NET APIs. Endpoint-local try-catches are themselves a finding — some routes catch, some don't; fix the global handler and delete them.
2. **Convention-test scan scope.** A test scanning only `ApiAssembly` misses `DomainAssembly` violations. Verify cohort discovery covers what the rule intends, and that the name says what it actually covers (`ApiTypes_MustNot...`, not `Types_MustNot...`).
3. **Domain guards vs application validators** — drift or missing coverage between the pair.
4. **CQRS boundaries** — EF leaking into queries, Dapper into commands.
5. **Dead code** — unused attributes, unreachable branches, ghost database columns a migration half-dropped (Dapper queries can silently reference leftovers via fallbacks), no-op methods.
6. **Duplicate logging** across endpoint and handler. Handler is authoritative; endpoint null-check logs before a 404 are the legitimate exception.
7. **Security** — injection vectors, authentication gaps, sensitive data exposure.

## The adversarial-verify workflow

For "thorough"/"comprehensive"/"audit everything" requests, and only when multi-agent orchestration has been opted into:

1. **Find** — one finder per subsystem. The eight that map to this repo: security/auth (gateway assertion, scopes, MFA, owner-only policy), CQRS/domain, data-access/persistence, eventing/outbox/Service Bus, payload-capture/PII, build/CI/reproducibility, concurrency/correctness, convention-test rigor. Tell each finder: the bar is high, ground every finding in `file:line` with quoted evidence, and **zero findings is an acceptable answer**.
2. **Dedup** — merge by file + normalized title in plain code (a barrier is correct here).
3. **Verify** — three independent skeptics per candidate, each with a distinct lens:
   - *code-truth*: re-read the path and its callers;
   - *defense-in-depth*: is it already covered by a guard, validator, constraint, or convention;
   - *exploitability*: is it actually reachable.
   Each is instructed to **refute**. Keep a finding only on a 2-of-3 majority.

Most candidates on this codebase are false positives. The value is in the filter, and in the small number of presence-vs-behaviour gaps that survive it.

## Known false positives — do not re-raise without new evidence

- **The cancel-path "stock double-restore"** — blocked by the order state machine.
- **`Money.Subtract` negative escape** — routes through `Create()`, which rejects negatives.
- **The AppHost `packages.lock.json` exemption** — intentional; Aspire injects host-RID packages. See `docs/DECISIONS.md`.
- **"Convention tests assert presence, not behaviour"** — as a blanket claim, stale; most have been hardened to IL/SQL behaviour checks. Specific new instances are still valid findings.

Never trust a score embedded in skill or doc text — the live number is in `docs/ARCHITECTURE_REVIEW.md`, and even that is a claim to verify, not a fact.

## Recurring finding classes

**Presence-vs-behaviour convention gaps** (the recurring class). Real instances found here: injecting `IOwnerOnlyPolicy` without invoking it; injecting `ICacheInvalidator` without calling it; declaring a `Guid` Id without minting it via `Guid.CreateVersion7()`; enforcing the negative CQRS rule (command handlers must not use `IDbConnection`) without the positive one (must depend on `ApplicationDbContext`). Fix pattern: IL/SQL scanning via `ContainsCallToMethod(il, module, name)` over constructor and method IL including async state machines. Prefer behaviour checks over name or source-text scans.

**Dead DataAnnotations.** `[Required]`/`[StringLength]`/`[Range]` on DTOs validated by `IValidator<T>` are never evaluated and mislead readers. Remove them *and* their `Validator.TryValidateObject()` unit tests — those tests should be deleted, not fixed; they test dead validation.

**Value objects without `IEquatable<T>`** — `Equals`/`GetHashCode` overridden but boxing in every LINQ comparison.

**Fixes break status-code assertions.** Changing an exception mapping (e.g. introducing `DomainRuleException` → 409) breaks integration tests asserting `HttpStatusCode.BadRequest`/`InternalServerError`. Search test files and update assertions as part of the fix, not as a follow-up.
