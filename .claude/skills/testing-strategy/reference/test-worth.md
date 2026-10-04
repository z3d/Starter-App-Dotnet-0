# Is this test worth having

Agents write every test in this repo, and a generated test costs nothing to write and something every day after: it is reviewed, run on every push and rewritten when the requirement moves. The suite is also the only thing that tells an agent it broke something. So the answer is never "delete the tests" and never "test every branch": each test has to be able to fail, and has to be worth its cost.

## Five questions before a test is merged

1. **Does it check behaviour or implementation?** If a refactor that keeps the behaviour would break it, it is in the way. A test that a setter sets, a mapper maps or a mock was called is implementation. The exception is a test that pins a decision on purpose (below).
2. **Would anyone notice if it were gone?** Name the bug it would catch. If you can't, it isn't a test yet.
3. **Is it a copy?** Extend the existing test or add a case to its data before adding a twin under another name.
4. **Does it protect something that hurts in production?** Money, a regulator's rule, a person's privacy, another tenant's data, a record that must not change. Those are kept even when they are slow.
5. **How long does it take?** An agent reruns the fast set many times in one change. Anything that needs a container, a browser or a clock belongs outside it (`dotnet test --filter "FullyQualifiedName!~Integration"` is the fast set).

## It must be able to fail

Show it red once: break the code or the fixture it guards, see the intended failure message, restore, see green. Say in the commit message what you broke. A test that has never failed has not been shown to test anything.

The ways a test here has passed on nothing, all found in the October 2026 audit:

- Seven `*_PropertiesTest` facts that proved a property setter works.
- Two derivation conventions that return early in the template and showed as passed, not as not applicable.
- A perf gate whose setup traffic counted against the list endpoints' budgets.

The general forms, to look for in review:

- **A silent skip.** `if (await x.count())`, `if (!condition) return;`, `test.skip(...)` decided at run time, a `try` that swallows. Seed the state so the path can't be missed; where a test truly doesn't apply, make it a named skip that shows as skipped.
- **A discovered set that can be empty.** Any loop or filter over found types, files, rows or scripts asserts a floor first.
- **A matcher too narrow to see the thing.** A convention's matcher gets a self-test: a probe in the test assembly that it must find.
- **A name the body doesn't keep.** "IsLogged" captures the log; "Erases" calls the erasure, not a `DELETE`.
- **An assertion that is true either way.** A count that can only grow, a value compared with itself, a check that accepts both outcomes.
- **A race.** "Nothing was written" is asserted only after something later is seen to have been written.

## End-to-end checks run the real shape

The Aspire facts are the deployed shape here (API, outbox, Service Bus, Functions). The k6 gate has a budget per endpoint and tags setup traffic `setup` so it stays out of the figures.

## What is left alone

`tests/StarterApp.Tests/Consistency/` (advisory reports the owner wants), every convention test (each pins a decision; widen its matcher, never loosen it), and tests that pin wording, numbers or ordering a derived project relies on.

A weak test in one of those is strengthened, never removed. If a test seems to lock in a bad decision, the decision is what to raise (in `docs/DECISIONS.md` or with the owner), not the test.

## What can't be tested here

Say so in `docs/ARCHITECTURE_REVIEW.md` rather than writing a test that pretends: a sign-in against a provider with no test tenant, a run on a device nobody can build for.
