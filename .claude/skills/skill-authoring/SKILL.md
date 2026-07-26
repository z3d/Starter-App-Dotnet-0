---
name: skill-authoring
description: Conventions for adding or modifying agent skills in this repo — the four-tier progressive-disclosure structure, what earns tokens at each tier, safety gates for operational skills. Use when creating a new skill or restructuring an existing one.
user-invocable: false
---

# Skill Authoring Conventions

Skills here follow four tiers of progressive disclosure. Each tier has a budget, and content lives at the **deepest tier that still gets it read in time**:

1. **`CLAUDE.md`** — always loaded. Commands, recorded decisions with re-add triggers, cross-cutting gotchas, non-inferable domain facts. Nothing that also lives in a skill.
2. **Frontmatter `description`** — always loaded; it is the trigger. State *when to use the skill*, not just what it covers. A vague description means the skill loads at the wrong time or never.
3. **`SKILL.md` body** — loaded when triggered. A **~40–60 line entry point that routes**: the rules an agent must not get wrong, each with its one-line why, plus a Depth table linking to references. Not the place for the depth itself.
4. **`reference/*.md`** beside the skill — loaded only when followed. Full code shapes, failure-mode catalogues, subsystem narratives. Depth is *relocated* here, never deleted.

What is deleted rather than relocated: generic engineering behaviour (reproduce-then-fix, run the tests, don't guess) — the model does this natively — and synthetic examples that shadow real code. Point at the real handler or `docs/exemplars/` instead.

Shared context lives in `CLAUDE.md`, referenced not restated — duplicated context drifts and then disagrees. End every skill with **Related skills**.

**Mirror rule:** every skill file — including each `reference/*.md` — exists in both trees (`.claude/skills` and `.agents/skills`), identical modulo the doc-name/skills-path tokens. `AgentDocsConventionTests` compares the *file sets* and canonicalized content, so a reference file without its twin fails the build. Edit both sides in the same change.

## Operational skills (anything that executes a workflow)

- **Phase structure with an explicit approval gate:** context-gathering → analysis → plan → STOP for approval → execute. Anything destructive, externally visible, or production-affecting sits behind the gate.
- **Environment-safety preamble:** pin the target environment/subscription first, re-pin on every switch of operation type. Never rely on ambient context.
- **Verification-link-first reporting:** every claim carries what's needed to confirm it independently — the exact query, blob name, git command.
- **Generate, don't execute** where feasible; destructive actions only on explicit request behind a confirmation checklist.
- **Vetted tools over raw CLI**; improvised CLI is the fallback.
- **Reuse accumulated knowledge:** investigation skills read and update `docs/investigations/` rather than starting from zero.

## When skills multiply

Once two skills overlap, add `maturity` (stable/experimental) and `supersedes` frontmatter, and fold the loser's unique content into the winner before retiring it.

## Related skills

- `development-workflow` — the local workflow these conventions plug into
- `testing-strategy` — how convention tests are written and proven
