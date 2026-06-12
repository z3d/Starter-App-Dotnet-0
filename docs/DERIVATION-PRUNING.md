# Pruning a Derived Project

This template ships deliberately heavy: the sample domain teaches the patterns, and the
convention fleet is the product. A project derived from it SHOULD prune — most slices will not
need every pattern. What separates safe pruning from silent regression is discipline, not
taste. These rules were proven out by a production derivation's complexity review and are
binding on any fork that wants to stay supportable.

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
5. **Keep read-path seams until first use is decided, then decide once.** Caching and the
   read-model split look removable in a write-heavy slice; they are cheap to keep and
   expensive to re-thread. Decide deliberately ("no cacheable reads in this slice, remove the
   wheel, keep the chassis") and record it — don't let the seam rot half-removed.

## Common prune candidates, with their re-add triggers

| Candidate | Usually safe to prune when… | Named re-add trigger |
|-----------|------------------------------|----------------------|
| Sample domain (Customer/Product/Order) | immediately — it exists to be replaced | n/a (it never comes back) |
| Owner/tenant scoping | the fork serves exactly one tenant by construction | a second tenant/brand, or external exposure of per-caller data |
| Distributed caching | the slice has no cacheable reads | first read endpoint with measurable repeat traffic |
| Feature toggles | the fork deploys continuously with instant rollback | first change that needs dark-launch or a kill switch |
| Perf gate thresholds | the fork's SLOs differ | recalibrate, don't delete — a red gate found a real defect here on its first night |

What is **not** prunable without a recorded decision: the payload capture/audit posture, the
outbox (publish-after-write is how events get lost), validator coverage, and the convention
fleet that enforces whatever subset you keep.
