---
paths:
  - "dev/StarterApp.AppHost/**"
  - "tests/StarterApp.AppHost.Tests/**"
  - "**/Dockerfile"
  - "src/StarterApp.ServiceDefaults/**"
---
# Aspire, the AppHost and container images

Traps that bite when the AppHost, its tests or an image build is touched. Each cost a session, and none shows up in a diff.

- **xUnit builds the collection fixture even when every fact in the collection is skipped.** An `[AspireFact]` skip alone still boots the distributed app. `AspireE2EFixture.InitializeAsync` returns early when `STARTERAPP_ASPIRE_TESTS` is unset; keep that guard if the fixture is rewritten.
- **Container image builds on a CRLF working tree fail formatting inside the image.** `EnforceCodeStyleInBuild` runs in the Dockerfile's build stage. Pass `-p:EnforceCodeStyleInBuild=false` to the in-container build or normalise line endings first.
- **The Service Bus emulator exits 139 when several worktree stacks are running.** Each worktree's AppHost creates its own persistent container set; at around eight the emulator segfaults and the outbox and Functions facts fail for reasons unrelated to the change. Remove stale sets (`podman ps -a`, filter by the AppHost hash suffix) before an Aspire run.
- **A value hardcoded in the AppHost is the last word.** `WithEnvironment(...)` lands in the child process as an environment variable, which outranks `appsettings` and user secrets, so it silently overrides anything a developer or a test fixture set. Anything a test may need to vary reads `builder.Configuration["Key"]` in the AppHost with the literal as the fallback.
