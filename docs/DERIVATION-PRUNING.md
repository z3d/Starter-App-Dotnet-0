# Pruning a Derived Project

This template ships deliberately heavy: the sample domain teaches the patterns, and the
convention fleet is the product. **A derived project MUST prune.** Once it is derived, anything
that was there only to teach is vestigial, and vestigial code misleads the agents that maintain
the project: they copy its shapes, keep its rules, and route new work through it.

## When

- **At the first module, the sample goes, whole.** The day a derived project's first real module
  lands under `Application/Modules/`, the Customer/Product/Order sample is removed end to end in one
  change: entities, events, handlers, endpoints, EF configurations, Functions and their
  subscriptions, tests, k6 and DAST seeds, the sample sections of `scripts/smoke-test.sh`, reporting
  SQL, a migration dropping its tables, and
  every rule, skill and doc that described only the sample. It is not "replaced module by
  module" and it is not kept as a reference: the template is the reference. The
  `<App>.Domain` project goes with it, whole: the base types it built on are in
  `<App>.SharedKernel`, and a project named Domain with no domain in it reads as anemic or
  vestigial to the next reader.
- **In the same change, every support capability is decided.** Owner scoping, the distributed
  cache and Redis, feature toggles and the rest either have a module consumer or are removed
  with a re-add trigger. None is left in place waiting for a use. Tenant isolation is not one of
  them (below).
- **The decision record stops calling the project a layered monolith.** `docs/DECISIONS.md`
  carries "Layered monolith, not a modular monolith (design on file, not adopted)", which is
  true of the template and its sample. The first module meets its re-add trigger, so in the same
  change the entry is replaced with one that records the project as a modular monolith (one
  schema and one `Contracts/` namespace per module, held by `ModuleConventionTests`), the
  alternative rejected and its own re-add trigger. Kept unchanged, it tells the next reader the
  opposite of what the code does; derived projects carried it for weeks before anyone noticed.
- **The template's rules become the project's rules only where they fit.** The template's
  "a seam with one implementation is an exemplar" rule protects the template's teaching seams.
  In a derived project a seam stays only when it is a real boundary (a module's `Contracts/`
  interface) or a module uses it.

`DerivationConventionTests` enforces the first two mechanically: in the template its rules are skipped as not
applicable (no modules), and in a derived project it fails from the first module until the sample types, the
`<App>.Domain` project, any unused `ICacheable` / owner-scoping markers and the web app's sample page
(`web/src/sample`) are gone.

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
6. **Never keep a wrong name or shape to keep syncs clean.** A project named for what it no longer
   holds, or a seam kept only because the template has it, costs every reader; a merge conflict
   costs one agent one resolution. Fix the design. A fix that belongs to every derived project goes
   to the template first and comes down on the next sync (the shared kernel split was one); a fix
   that is the derived project's own is made there and its divergence recorded in the fork's
   decisions, so the next sync knows it was deliberate.

## Common prune candidates, with their re-add triggers

| Candidate | Usually safe to prune when… | Named re-add trigger |
|-----------|------------------------------|----------------------|
| Sample domain (Customer/Product/Order) and the `<App>.Domain` project that holds it | required at the first module — it exists to be replaced; `<App>.SharedKernel` stays | n/a (it never comes back) |
| The web app's sample page (`web/src/sample`) and the `Sample` rail item | with the sample domain it drives; the shell, `src/lib/auth.ts`, `src/lib/api.ts`, the generated client, the primitives, the image and the browser-test fixtures stay | n/a (the project's own screens take its place) |
| The smoke test's Products, Customers and Orders sections (`scripts/smoke-test.sh`) | with the sample domain whose routes they call; the token, TLS and health parts stay, and the project's own first read and write take the sections' place | n/a (the project's own routes take their place) |
| Tests that borrow the sample's routes or fields to test shared code: `IdentityProviderOutageTests` (`/api/v1/products`), `PayloadSecretMaskerTests` (`orderId`), `web/src/lib/resilient-fetch.test.ts` (`/api/v1/customers`), the assistant's example find and proposal, their prompt notes and recorded responses, and the sample entries in `payload-log-fields.txt` | moved onto the project's own routes and fields in the same change, not deleted: they test shared code | n/a |
| Per-person owner scoping (`owner_subject`, `IOwnerOnlyPolicy`, `IOwnerAuthorizedMutation`) | no module scopes a row to the person who created it, inside a tenant | a module whose rows belong to one person and must be hidden from that person's colleagues |
| Distributed caching | the slice has no cacheable reads | first read endpoint with measurable repeat traffic |
| Feature toggles | the fork deploys continuously with instant rollback | first change that needs dark-launch or a kill switch |
| Perf gate thresholds | the fork's SLOs differ | recalibrate, don't delete — a red gate found a real defect here on its first night |

## Tenant isolation is never pruned in a multi-tenant product

Tenant isolation and per-person owner scoping are two different things, and only the second is a prune
candidate. Tenant isolation is the database's: every table of a module that holds a tenant's rows has a
`tenant_id`, a policy whose `USING` and `WITH CHECK` compare it with the session's `app.tenant_id`, and
`ENABLE` and `FORCE ROW LEVEL SECURITY`, made in the same Expand script that creates the table. The API and
the worker sign in as logins that cannot switch it off, and bind the tenant from the validated token (or,
in the worker, from the event) whenever a connection opens. `TenantRowSecurityTests` reads every table from
the catalog and fails one with a `tenant_id` and no forced policy, and one with neither a `tenant_id` nor a
stated reason in `TenantRowSecurityRules.PlatformTables`; it keeps checking after the sample is gone,
because it selects by the rule, not by the sample's names. Removing the sample removes its policies with
its tables; nothing else of this goes.

A project that already had tenant policies written with `USING` alone needs no new policies to take this:
`ALTER POLICY ... WITH CHECK (<the same expression>)` in an Expand script gives the catalog rule the shape
it reads and changes nothing for a caller, since PostgreSQL already applied `USING` to a written row.

A derived project that will only ever serve one business still keeps it. The tenant is then the one
business, bound from the token's tenant claim like any other: the columns, policies and logins cost a
module nothing, and taking a second business later would otherwise mean a migration of every table with
rows in it. The single-tenant decision, if it is ever made, is recorded in the project's
`docs/DECISIONS.md` with its date, who made it, and the re-add trigger (a second business, a reseller, or a
second brand on the same deployment), and the policies stay: what changes is only that one tenant exists.
"Not multi-tenant" is never a reason to drop a policy or to list a table with rows in `PlatformTables`.

What is **not** prunable without a recorded decision: the payload capture/audit posture, the
outbox (publish-after-write is how events get lost), validator coverage, and the convention
fleet that enforces whatever subset you keep.

`TenantSettings` (the tenant's colour theme, `/api/v1/settings/appearance`, the Settings page) is
not sample. It sits in `<App>.Domain` only because the template has no module; when that project
goes, the aggregate, its query, command, validators and endpoint move into the module that owns the
tenant, its table into that module's schema, and its convention-list entries move with it.
