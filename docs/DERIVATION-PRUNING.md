# Pruning a Derived Project

This template ships deliberately heavy: the sample domain teaches the patterns, and the
convention fleet is the product. **A derived project MUST prune.** Once it is derived, anything
that was there only to teach is vestigial, and vestigial code misleads the agents that maintain
the project: they copy its shapes, keep its rules, and route new work through it.

## When

- **At the first module, the sample goes, whole.** The day a derived project's first real module
  lands under `Api/Modules/`, the Customer/Product/Order sample is removed end to end in one
  change: entities, events, handlers, endpoints, EF configurations, Functions and their
  subscriptions, tests, k6 and DAST seeds, reporting SQL, a migration dropping its tables, and
  every rule, skill and doc that described only the sample. It is not "replaced module by
  module" and it is not kept as a reference: the template is the reference.
- **In the same change, every support capability is decided.** Owner scoping, the distributed
  cache and Redis, feature toggles and the rest either have a module consumer or are removed
  with a re-add trigger. None is left in place waiting for a use.
- **The template's rules become the project's rules only where they fit.** The template's
  "a seam with one implementation is an exemplar" rule protects the template's teaching seams.
  In a derived project a seam stays only when it is a real boundary (a module's `Contracts/`
  interface) or a module uses it.

`DerivationConventionTests` enforces the first two mechanically: in the template it passes (no
modules), and in a derived project it fails from the first module until the sample types and any
unused `ICacheable` / owner-scoping markers are gone.

## The discipline

1. **Every removal gets a named, falsifiable re-add trigger, recorded in the fork's
   architecture review.** "Re-add owner scoping when a second brand onboards" is a trigger;
   "re-add if needed" is not. A removal without a trigger is a deletion nobody can ever
   safely reverse.
2. **Check human and ops consumers before removing support artifacts.** Code searches find
   code consumers; they do not find the support engineer who greps the audit feed every
   morning. Ask the people who operate the system before deleting anything they might read —
   one derivation removed its audit feed on a zero-code-consumers proof and had to restore it
   because ops read it daily.
3. **A re-add ships as one change.** If a pruned capability comes back, it returns complete
   (code + tests + conventions + docs) in a single reviewed change — not as a drip of partial
   restorations that each look too small to review properly.
4. **Never leave an event published with no subscriber.** A real broker discards an unmatched
   publish instantly while the outbox row reads processed: a silent shredder, not a backlog.
   This repo pins the rule mechanically (every contract must be covered by a subscription
   filter or an explicit publish-only allowlist entry); keep that convention in forks.
5. **Decide each read-path seam once, at the first module.** Caching and the read-model split
   look removable in a write-heavy slice. Decide deliberately (either a module uses it, or it is
   removed with its trigger) and record it. Don't let a seam rot half-removed.

## Common prune candidates, with their re-add triggers

| Candidate | Usually safe to prune when… | Named re-add trigger |
|-----------|------------------------------|----------------------|
| Sample domain (Customer/Product/Order) | required at the first module — it exists to be replaced | n/a (it never comes back) |
| Owner/tenant scoping | no module scopes a row to the person who created it | a second tenant/brand, or external exposure of per-caller data |
| Distributed caching | the slice has no cacheable reads | first read endpoint with measurable repeat traffic |
| Feature toggles | the fork deploys continuously with instant rollback | first change that needs dark-launch or a kill switch |
| Perf gate thresholds | the fork's SLOs differ | recalibrate, don't delete — a red gate found a real defect here on its first night |

What is **not** prunable without a recorded decision: the payload capture/audit posture, the
outbox (publish-after-write is how events get lost), validator coverage, and the convention
fleet that enforces whatever subset you keep.
