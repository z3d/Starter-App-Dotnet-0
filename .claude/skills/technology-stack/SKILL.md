---
name: technology-stack
description: Why the custom mediator exists, its dispatch pipeline, and where dependency versions live. Use when adding dependencies or working on the mediator pipeline.
user-invocable: false
---

# Technology Stack

Every package version is pinned in `Directory.Packages.props` — read it rather than any list in a doc. `.csproj` files carry versionless `PackageReference` entries; a `Version=` attribute is a CPM error.

## The rules

- **FsCheck is on 3.x** — a major bump from 2.x with a different API surface (`Gen.OneOf` and friends). Don't paste 2.x examples.
- **The Aspire AppHost SDK version must match the `Aspire.Hosting.AppHost` package version** — a convention test pins the pair.
- **MediatR is prohibited** (commercial licence); the custom mediator in `Api/Infrastructure/Mediator/` replaces it. The request pipeline is, in order: feature-toggle gate → validators → pipeline behaviors (registration order, first = outermost) → handler.
- **`[FeatureToggle("name")]` is checked before validators and every behavior**, so a disabled feature is never served from cache — `FeatureDisabledException` → 503. Request types only, unique names, explicit config entry per toggle (convention-enforced); missing entry means enabled.
- **Don't reintroduce per-call reflection into dispatch.** The typed wrapper is built once per request type and cached; every send after that is a dictionary lookup plus a typed call. → [reference/mediator-internals.md](reference/mediator-internals.md)

## Depth

| Topic | Reference |
|---|---|
| Dispatch internals: wrapper caching, `SendAsync` shape, behavior composition | [reference/mediator-internals.md](reference/mediator-internals.md) |

## Related skills

- `cqrs-patterns` — the handler contracts this dispatches to
- `data-access` — EF Core and Npgsql configuration
