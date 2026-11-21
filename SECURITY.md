# Security

## Reporting a vulnerability

Report it privately through GitHub: the repository's **Security** tab, then **Report a vulnerability**. Please do not open an issue, a discussion or a pull request about it.

Say what you found, where (file, endpoint or workflow), how to reproduce it, and what an attacker could do with it. You will get an answer in the advisory thread, and a fix is credited to you there unless you ask otherwise.

## In scope

The code in this repository, as it is on `main`: the API, the migrator, the Functions worker, the web app, the AppHost's publish shape, the GitHub workflows and the local hooks.

## Out of scope

- Projects derived from this template. Each is its own codebase; report to whoever runs it.
- Vulnerabilities in a dependency with no path through this code. Report those to the dependency. If this repository pins a version you know to be vulnerable, that is in scope.
- The local development stack's fixed demo credentials (the Keycloak realm, the emulators). They exist to be public and never leave a developer's machine.

## What already runs

CodeQL, a full-history secret scan (`gitleaks`) and a DAST scan run as GitHub workflows (`.github/workflows/`), and `dotnet restore` fails on a package with a known advisory. Findings from those go through the same private route.
