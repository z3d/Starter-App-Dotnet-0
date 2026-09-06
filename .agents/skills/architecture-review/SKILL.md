---
name: architecture-review
description: Perform a thorough architecture review of a .NET project examining structure, maintainability, clarity, robustness, and goal achievement. Use when the user asks to review, audit, or examine a codebase.
disable-model-invocation: true
user-invocable: true
argument-hint: [project-path]
---

# Architecture Review

Review the project at `$ARGUMENTS`, or the current working directory if no argument is given.

**Start by reading `docs/ARCHITECTURE_REVIEW.md`** — open findings, accepted limitations, current score. Evidence, resolutions, and dismissed false positives live in the dated records under `docs/reviews/`; check the relevant record before re-raising anything. Write your review as a new dated record there and update the living file when you finish; it is the sync point across concurrent sessions.

**This codebase's dominant failure mode is plausible-but-wrong findings, not missed bugs.** It is mature and hardened, with a long history of dismissing HIGH/CRITICAL candidates. Structure the work to refute: zero findings is an acceptable answer, a high score is a prompt to verify against code rather than trust it, and the known false positives in [reference/review-playbook.md](reference/review-playbook.md) must not be re-raised without new evidence.

## Approach

1. **Explore in parallel** — one agent per area (structure/DI, domain, application, infrastructure, API + tests), each reading every file in its area.
2. **Analyse** with the bias list in the playbook — exception flows end-to-end first; they are the largest source of bugs in .NET APIs.
3. For an exhaustive audit (only when multi-agent orchestration is opted in): find → dedup → adversarially verify, three skeptics per candidate with distinct lenses, keep on 2-of-3 majority. Details in the playbook.

## Findings format

```
### N. [SHORT TITLE]
**Severity: High|Medium|Low** | Files: `file.cs:line`
[1-3 sentences: problem and impact]
**Fix**: [concrete, not vague advice]
```

High = bugs, security, data integrity. Medium = inconsistencies, test gaps, growth hazards. Low = clarity. Close with a summary table, a short assessment, a fix order by impact, and verification steps.

Don't penalize absent features unless the project claims them; don't recommend a library without a concrete problem. Dead-code removal is always fair game.

## Before marking any finding resolved

Prove the regression test fails on the regression it guards: inject it, confirm the intended failure message, revert, confirm green. `Assert.NotEmpty` on any filtered set. After reverting by file copy, `touch` the source — a stale mtime makes MSBuild skip the rebuild. Full discipline in `testing-strategy`.

## Depth

| Topic | Reference |
|---|---|
| Analysis bias list, adversarial-verify workflow, known false positives, recurring finding classes | [reference/review-playbook.md](reference/review-playbook.md) |

## Related skills

- `testing-strategy` — writing the regression test a finding needs
- `skill-authoring` — the safety-gate conventions this skill follows
