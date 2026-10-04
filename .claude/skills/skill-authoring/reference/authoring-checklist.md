# Skill authoring checklist

From Anthropic's skill authoring guidance (`platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices`, `code.claude.com/docs/en/skills`), as applied in this repo.

## Frontmatter

- `name`: lower-case letters, numbers and hyphens; at most 64 characters; the same as the folder.
- `description`: at most 1,024 characters; third person ("Turns...", not "I can..." or "You can..."); says what the skill does **and** when to use it; carries the words a person would use when they need it.
- `user-invocable`: `true` only for a procedure a person calls by name.
- Frontmatter must parse as YAML. A colon followed by a space inside an unquoted description breaks it, and a skill whose frontmatter fails to parse loads with every field dropped and nothing warning you.

## Body

- Assume the reader is capable: include only what it can't work out from the repo.
- Match the freedom to the risk: exact commands where one wrong step breaks something (a migration, a deploy), principles where judgment is the point (a review).
- Steps in the order the work happens; a checklist for anything with more than a few steps.
- For work with a quality bar, a feedback loop: do, check, fix, check again.
- No time-sensitive wording; no two names for one thing; no options without a default.
- Paths with forward slashes; real files in this repo rather than invented examples.

## Structure

- `SKILL.md` is the entry point and routes; depth lives in `reference/` files loaded only when followed.
- References one level deep from `SKILL.md`. A reference over about 100 lines starts with a contents list.
- A script the skill runs lives beside it and is run, not read into context; say which.

## Before it is merged

- Three phrasings that should load it, one that should not, checked against the description.
- A fresh agent given a real task with only the repo: note where it stumbled and what it never read.
- No rule restated from `AGENTS.md` or another skill; link instead.
- The lesson it came from named in the commit message.
