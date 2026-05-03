# Consistency Tests: Architecture Drift Detection for AI-Maintained Codebases

Status: Draft  
Date: 2026-05-03

Most software tests answer one of two questions:

- Does the behavior work?
- Did we break a rule?

Those questions matter, but they miss a third category that becomes painfully important as a codebase grows:

> Is this new code shaped like the rest of the system, or is it quietly drifting?

That question is especially important in codebases maintained by AI agents. Agents are very good at following explicit rules. They are less reliable at inferring taste, remembering local style, or noticing that one implementation is becoming structurally unlike its siblings.

This project uses convention tests for the hard rules. Every command and query has a validator. Query handlers use Dapper, not EF Core. Command handlers use EF Core, not `IDbConnection`. Dapper queries do not use `SELECT *`. Docker Compose must keep parity with Aspire-owned infrastructure.

Those are deterministic rules. They belong in tests that fail the build.

But not every architectural concern is that crisp. Some code can be unusual for a good reason. A handler may need one more dependency. An EF Core configuration may need an extra owned value object. A query may have a legitimate join shape that the other queries do not.

That is where consistency tests come in.

## The Idea

Consistency testing is an advisory layer that measures structural drift across repeated code shapes.

In this repository, the consistency layer lives under [`src/StarterApp.Tests/Consistency/`](../../src/StarterApp.Tests/Consistency/) and compares cohorts against pinned exemplars in [`docs/exemplars/`](../exemplars/):

- Command handlers
- Query handlers
- EF Core entity configurations

The goal is not to say "all files must be identical." That would be brittle and wrong. The goal is to surface surprising outliers early, when a reviewer can still ask a useful question:

> Is this different because the feature is different, or because the implementation drifted?

That distinction matters. A hard rule should be promoted into a convention test. A suspicious shape should be reported for review. Consistency tests are for the second case.

## Convention Tests vs. Consistency Tests

I think the most useful framing is this:

> Convention tests enforce rules. Consistency tests detect surprise.

A convention test is a gate. It says: this must always be true.

Examples from this project:

- Every command/query must have a validator.
- Query handlers must not use EF Core.
- Command handlers must not use Dapper.
- Cacheable by-id queries must have deterministic cache keys.
- Production logs must not write raw body placeholders.

A consistency test is a signal. It says: this member of a cohort is far away from the exemplar set.

Examples:

- This command handler has many more private helper methods than the exemplars.
- This query handler has an unusual dependency shape.
- This EF configuration has a mapping shape that does not resemble the pinned configurations.

That signal might become a new convention test later. But it should not start as one unless the rule is genuinely deterministic.

## Why AI-Maintained Code Changes the Tradeoff

For human-maintained code, requiring a validator for a trivial delete command can feel like busywork. A human can look at `DeleteProductCommand` and reasonably ask whether `Id > 0` deserves a whole validator.

For an AI-maintained codebase, that judgment call is a source of drift.

The stronger rule is:

> Every command and query has a validator.

That rule is boring. It is mechanical. It is also exactly the kind of rule an agent can follow perfectly.

Consistency tests handle the other side of the problem. They give agents and reviewers a way to inspect "soft architecture" without pretending soft architecture is always a hard invariant.

This split is the core pattern:

- Mechanical invariants go into convention tests.
- Structural surprise goes into consistency tests.
- Repeated advisory findings get promoted into convention tests.

## How the Cohort Model Works

A cohort is a group of files or types that are expected to share a recognizable shape.

For each cohort, the test suite defines:

- How to discover members.
- How to extract a structural fingerprint.
- Which members are pinned exemplars.
- Where the exemplar documentation lives.

The consistency toolset then compares all members against the exemplar set.

For example, command handlers are measured against exemplars documented in [`docs/exemplars/command-handlers/README.md`](../exemplars/command-handlers/README.md). Query handlers and EF configurations have their own exemplar sets.

This is important: exemplars are not implicit. They are deliberately named and documented. The governance tests make sure exemplar names resolve to real source files and that each exemplar has written justification. Without that, the system would quietly turn into "whatever happened to exist first."

## What Gets Measured

The implementation currently uses three layers.

### 1. Structural Distance

Structural fingerprints turn a cohort member into a vector of features. The exact feature set depends on the cohort.

For a handler, useful features might include:

- Dependency count
- IL byte size
- Private method count
- Whether it logs
- Whether it invalidates cache
- Entity load count
- Try/catch count

The scorer computes distance from the exemplar centroid. It uses z-scoring across the cohort and Mahalanobis distance with shrinkage so small exemplar sets do not collapse into singular covariance matrices.

That sounds fancier than the intent. The practical question is simple:

> Which files are unusually shaped compared with the exemplars?

### 2. AST/IL Shingle Similarity

The test suite also computes a shingle-style similarity over instruction shape. This catches structural resemblance that a flat feature vector can miss.

It is not trying to prove semantic equivalence. It is just another way to ask whether a member looks like the exemplar family.

### 3. Source-Token Embedding Similarity

Finally, source-token similarity catches vocabulary outliers: domain names, methods, DTOs, entity references, SQL terms, and so on.

This has obvious limits. Token overlap is not understanding. But as a review signal, it is useful. If a query handler's vocabulary is far away from the rest of the query-handler cohort, that may be fine. It may also be the first hint that the code is carrying the wrong responsibility.

## Why the Tests Are Advisory

The most tempting mistake is to turn every consistency score into a build failure.

That would make the system worse.

Architectural consistency is valuable because it protects local reasoning. But useful software sometimes needs local exceptions. A mature consistency-testing layer should create friction, not dogma.

In this project, deterministic rules live under `Conventions/`. Advisory drift checks live under `Consistency/`. The convention README says this directly:

```text
Keep advisory similarity/drift checks in Consistency/, not here.
```

That line is doing architectural work. It keeps the build from becoming a style prison while still making drift visible.

## The Feedback Loop

The workflow looks like this:

1. Add or change code.
2. Run normal tests and convention tests.
3. Review consistency reports for surprising outliers.
4. Decide whether the difference is intentional.
5. If the same advisory signal keeps mattering, promote it into a convention test.

That last step is the payoff.

Consistency tests are not the end state. They are a discovery mechanism for future rules.

When a drift signal becomes unambiguously bad, encode it as a hard convention. Until then, keep it advisory.

## A Concrete Example

Imagine a new query handler appears with three unusual properties:

- It injects a cache invalidator.
- It performs mutation-like work.
- Its SQL shape is far away from the query exemplars.

No single signal proves the handler is wrong. But together, they create a good review question:

> Is this actually a query, or did command behavior leak into the read side?

If the answer is "this is wrong," there may already be a convention test that catches it. If not, the team can add one. For example:

```csharp
[Fact]
public void QueryHandlers_MustNotInjectCacheInvalidator()
{
    // Deterministic rule goes here once the team agrees it should always hold.
}
```

The consistency layer helped discover the rule. The convention layer enforces it forever.

## What This Is Not

Consistency testing is not a replacement for:

- Unit tests
- Integration tests
- Property-based tests
- Architecture tests
- Code review
- Human taste

It also does not prove correctness. A perfectly consistent handler can still be wrong. A structurally unusual handler can still be exactly right.

The point is narrower and more useful:

> Consistency tests make architectural drift visible before it becomes ambient.

## Is This a New Kind of Testing?

Not exactly.

The ingredients already exist:

- Architecture fitness functions
- Convention tests
- Golden-master and approval testing
- Static analysis
- Similarity and clone detection

The useful part is the synthesis.

In application architecture, especially in AI-maintained codebases, it helps to give this pattern a name:

> Cohort consistency testing: compare repeated code shapes against documented exemplars, report structural drift as advisory signal, and promote stable findings into deterministic convention tests.

That name gives teams a place to put a kind of concern that otherwise gets lost in code review comments.

## The Takeaway

As AI writes more of the routine code, the important question shifts.

It is not only:

> Can the agent implement this feature?

It is also:

> Can the codebase make the right shape obvious enough that the agent keeps producing code we can reason about?

Convention tests help by encoding rules. Consistency tests help by measuring drift. Together, they turn architecture from a memory exercise into a feedback system.

That is the real trick: not making the codebase rigid, but making its shape legible.
