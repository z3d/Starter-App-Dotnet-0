# Contributing

[`AGENTS.md`](AGENTS.md) holds the rules for everyone who changes this repository, agent or person. This page covers only how a change travels from a branch to `main`. To run the stack, start with [`docs/GETTING-STARTED.md`](docs/GETTING-STARTED.md).

## Branches

- Name a branch for what it does: `feat/`, `fix/`, `chore/`, `docs/` or `refactor/`, then a few words (`fix/order-retry-id`).
- Keep it short-lived. One task, one branch, merged or dropped within a day or two.
- Work in your own worktree, never in a checkout someone else is using: `git fetch origin && git worktree add -b <branch> ../<dir> origin/main`.

## Commits

- The subject is a plain sentence that says what is now true ("The API answers 503 when the sign-in provider is down"). No `feat:` style prefixes.
- The body says why, and anything a reviewer could not see in the diff.
- An agent's commit ends with `Harness:` and `Model:` trailers. `.githooks/commit-msg` refuses one without them.

## From branch to main

1. Commit on its own, and check the hash.
2. Merge (squash by default for a pull request; a fast-forward for a local merge).
3. Push. `.githooks/pre-push` runs the full local gate first; a push of documentation alone skips it.
4. Clean up: remove the worktree and the branch.

Run each step as its own command, so a failed commit cannot be followed by a cleanup.

Before any push, grep the diff for names that must not appear in a public repository (customers, private projects, people):

```bash
git diff origin/main --unified=0 | grep -inE '<names you keep private>'
```

It should print nothing.

## Hooks

`dotnet build` points `core.hooksPath` at [`.githooks/`](.githooks/), so a fresh clone needs no setup. Read the hooks rather than a summary of them.

## Reporting a vulnerability

Not in an issue or a pull request. See [`SECURITY.md`](SECURITY.md).
