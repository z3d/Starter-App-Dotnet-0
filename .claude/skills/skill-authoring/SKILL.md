---
name: skill-authoring
description: Adding or changing agent skills in this repo, and turning something learned in a session (a review or audit finding, a correction from the owner, a trap that cost time) into the durable thing that stops it recurring - a test, a hook, a rule, a line in AGENTS.md or a skill. Covers the four-tier structure, what earns tokens at each tier, safety gates for operational skills and the authoring checklist. Use when creating or restructuring a skill, when the owner says "remember this", "make a skill for this" or "so it doesn't happen again", or at the end of a review, audit or incident.
user-invocable: true
---

# Skill Authoring Conventions

## Starting from a lesson

A lesson kept only in a conversation is lost when the context is summarised. Say it in one sentence ("when X, do Y, because Z happened"); if you can't name what went wrong without it, or the code, the git history or a skill already says it, there is nothing to keep. Then put it in the strongest place that fits, taking the first row that does:

| The lesson is | It becomes |
|---|---|
| Something a machine can check | A convention test or a `PreToolUse` hook in `.claude/hooks/`. An instruction is a request; a test or hook is enforcement |
| True only inside one module or folder | A path-scoped file in `.claude/rules/` (YAML `paths:` frontmatter, loaded when a matching file is touched) |
| Needed by every session, in one line | A line in `AGENTS.md`, in the section it belongs to; the file stays under 200 lines and repeats nothing a skill says |
| A procedure, a checklist or reference material needed sometimes | A skill. Read every skill's `description` first and add to the one that covers the job; a new skill is for a new job |
| About the owner, or about the agent harness and not this repo | The harness's own memory (Claude Code keeps one per project outside the repo) |

Most lessons end as a test or one line. This is Anthropic's guidance (`code.claude.com/docs/en/memory`, `features-overview`).

## The four tiers

Skills here follow four tiers of progressive disclosure. Each tier has a budget, and content lives at the **deepest tier that still gets it read in time**:

1. **`AGENTS.md`** — always loaded (Claude reads it through `CLAUDE.md`'s `@AGENTS.md` import). Commands, recorded decisions with re-add triggers, cross-cutting gotchas, non-inferable domain facts. Nothing that also lives in a skill.
2. **Frontmatter `description`** — always loaded; it is the trigger. State *when to use the skill*, not just what it covers. A vague description means the skill loads at the wrong time or never.
3. **`SKILL.md` body** — loaded when triggered. A **~40–60 line entry point that routes**: the rules an agent must not get wrong, each with its one-line why, plus a Depth table linking to references. Not the place for the depth itself. The rules come first in the body: after a context compaction only the start of an invoked skill may survive.
4. **`reference/*.md`** beside the skill — loaded only when followed. Full code shapes, failure-mode catalogues, subsystem narratives. Depth is *relocated* here, never deleted.

What is deleted rather than relocated: generic engineering behaviour (reproduce-then-fix, run the tests, don't guess) — the model does this natively — and synthetic examples that shadow real code. Point at the real handler or `docs/exemplars/` instead.

Shared context lives in `AGENTS.md`, referenced not restated — duplicated context drifts and then disagrees. End every skill with **Related skills**.

## Operational skills (anything that executes a workflow)

- **Phase structure with an explicit approval gate:** context-gathering → analysis → plan → STOP for approval → execute. Anything destructive, externally visible, or production-affecting sits behind the gate.
- **Environment-safety preamble:** pin the target environment/subscription first, re-pin on every switch of operation type. Never rely on ambient context.
- **Verification-link-first reporting:** every claim carries what's needed to confirm it independently — the exact query, blob name, git command.
- **Generate, don't execute** where feasible; destructive actions only on explicit request behind a confirmation checklist.
- **Vetted tools over raw CLI**; improvised CLI is the fallback.
- **Reuse accumulated knowledge:** investigation skills read and update `docs/investigations/` rather than starting from zero.

## Before a skill is merged

- **The trigger:** write three ways a person might ask for it and one request that should not load it; check the `description` against each.
- **The body:** hand a fresh subagent a real task of this kind with nothing but the repo, and read what it did. Where it went wrong is what the skill is missing; a paragraph it never needed is cut.
- **Registered:** a row in the "Where to look" table in `AGENTS.md`; the commit message names the lesson or need it came from.
- The limits and the full list are in [reference/authoring-checklist.md](reference/authoring-checklist.md).

## When skills multiply

Once two skills overlap, add `maturity` (stable/experimental) and `supersedes` frontmatter, and fold the loser's unique content into the winner before retiring it.

## Related skills

- `development-workflow` — the local workflow these conventions plug into
- `testing-strategy` — how convention tests are written and proven
