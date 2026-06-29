---
name: skill-authoring
description: Conventions for adding or modifying agent skills in this repo — skill types, structure, safety gates for operational skills, verification-first reporting. Use when creating a new skill or restructuring an existing one.
user-invocable: false
---

# Skill Authoring Conventions

Two kinds of skill live in this repo. **Reference skills** document how this repo's patterns work
(most skills here are this kind; architecture-review is the operational exemplar). **Operational skills** execute a workflow — investigations,
code generation, deployment checks. The rules below keep both kinds safe and consistent as the
skill set grows.

## All skills

- Frontmatter `description` states **when to use the skill**, not just what it covers — the
  description is the trigger.
- Shared context lives in `CLAUDE.md`, not duplicated across skills. A skill references the root
  doc instead of restating its rules; duplicated context drifts and then disagrees.
- End with a short **Related skills** list so agents can navigate between companions instead of
  rediscovering them.
- Mirror rule: every skill exists in both agent trees (`.claude/skills` and its mirror) with
  identical content modulo the agent-specific tokens. `AgentDocsConventionTests` enforces this —
  always create or edit both files in the same change.

## Operational skills (anything that executes a workflow)

- **Phase structure with an explicit approval gate.** Order the work context-gathering → analysis
  → plan → STOP for user approval → execute. Any step that is destructive, externally visible, or
  production-affecting sits behind the explicit gate — never bundled into an earlier phase.
- **Environment-safety preamble.** If the skill touches cloud resources, its first step pins the
  target environment/subscription explicitly, and re-pins on every switch of operation type.
  Never rely on ambient context for where a command lands.
- **Verification-link-first reporting.** Every claim in an investigation or report output carries
  the evidence needed to confirm it independently: the exact query, the blob/log name, the git
  command. A finding the reader cannot verify without re-deriving it is not finished.
- **Destructive actions only on explicit user request**, behind a confirmation checklist. Prefer
  generate-don't-execute — emit the SQL/script for review rather than running it — wherever
  feasible.
- **Vetted tools over raw CLI.** When a purpose-built tool exists for an operation, the skill
  mandates it and documents its command surface; improvised raw CLI is the fallback, not the
  default.
- **Reuse accumulated knowledge.** Repeat-heavy investigation skills read and update the incident
  knowledge base (`docs/investigations/` — see its README) rather than starting every
  diagnosis from zero.

## When skills multiply

Once two skills overlap in purpose, add `maturity` (stable/experimental) and `supersedes`
frontmatter so there is never ambiguity about which one to use, and fold the loser's unique
content into the winner before retiring it.

## Related skills

- `development-workflow` — debugging and local CI workflow these conventions plug into
- `testing-strategy` — where convention tests are documented
