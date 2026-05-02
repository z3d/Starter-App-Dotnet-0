---
name: development-workflow
description: Debugging workflow, Docker dual-runtime requirements, dev tunnels, local CI. Use when debugging issues or working with deployment configuration.
user-invocable: false
---

# Development Workflow

## Code Quality Standards

### Testing Requirements
- ALWAYS run tests before committing changes
- All tests must pass
- Write tests for new domain objects following existing patterns
- Use integration tests for API endpoints

### Code Duplication Guidelines
- Duplication over wrong abstraction — bad abstractions are harder to fix
- Consider domain boundaries — similar code in different contexts should stay separate
- Evaluate coupling costs — sometimes shared code creates expensive coupling
- Optimize for readability — sometimes duplication is clearer than complex abstraction

## Debugging Workflow

When encountering bugs or errors, ALWAYS follow this structured approach:

### Step 1: Reproduce the Bug
- Write a failing test first (TDD approach)
- The test should fail for the right reason (exposing the bug)
- Run existing tests to see if they already catch the problem
- Document the exact steps to reproduce

### Step 2: Gather Complete Error Information
- Full exception stack trace (not just the first line)
- Exception type (e.g., `DbUpdateException`, `ArgumentException`)
- Actual error message and any inner exceptions
- Application logs for additional context

### Step 3: Analyze Root Cause
- Read the error message carefully — it often tells you exactly what's wrong
- Check database constraints (column types, precision, null constraints)
- Verify domain validation rules match database schema
- Look for data type mismatches

**Common issues**:
- `DECIMAL(5,4)` max value is `9.9999` — can't store `10.0`
- Percentage rates: use decimal format (`0.10` for 10%), not whole numbers
- String length violations: check `NVARCHAR(N)` limits
- Null constraint violations: ensure required fields are set

### Step 4: Fix with Validation
- Add domain-level validation to catch errors early
- Provide clear, helpful error messages explaining valid range/format and how to fix

```csharp
if (gstRate > 1.0m)
    throw new ArgumentOutOfRangeException(nameof(gstRate), gstRate,
        "GST rate must be a decimal value between 0 and 1 (e.g., 0.10 for 10%). " +
        "Database constraint: DECIMAL(5,4) with max value 9.9999.");
```

### Step 5: Add Tests
- Write tests that verify the validation works
- Test boundary conditions (edge cases)
- Test both valid and invalid inputs

### Step 6: Verify the Fix
- Run all tests: `dotnet test`
- Test the original reproduction case manually
- Verify error messages are clear and helpful

### Anti-Patterns to Avoid
- Guessing at fixes without understanding root cause
- Partial error messages — always get full stack trace
- Skipping tests — untested fixes often break later
- Silent failures — add validation that gives clear feedback
- Fixing symptoms instead of root cause

## Docker & Deployment Notes

### Dual-Runtime Requirement

The API must work both as a **standalone Docker container** and under **Aspire orchestration**. When making infrastructure changes, always verify both modes:

- **Connection strings**: Standardized on `database` key — Aspire injects this, Docker Compose sets it via environment variable, and `appsettings.Docker.json` provides it as fallback
- **Health checks**: `app.MapHealthChecks("/health")` must be mapped unconditionally — Aspire's `MapDefaultEndpoints()` only maps in Development environment, which is insufficient for Docker healthchecks
- **Service registration**: All DI registrations must work without Aspire service discovery present — use conditional checks or fallback defaults where Aspire provides configuration

### Smoke Testing

After deploying or modifying infrastructure, run the smoke test script against the live environment:

```bash
# Docker Compose (default: http://localhost:8080)
./scripts/smoke-test.sh

# Aspire (HTTPS with dev certs)
./scripts/smoke-test.sh https://localhost:7286

# Any environment
./scripts/smoke-test.sh https://staging.example.com
```

The script tests all CRUD endpoints, validators (email, currency, OrderId), conflict responses (409), and not-found responses (404). It uses unique test data per run and exits non-zero on failure — suitable for CI post-deploy gates.

### .NET 10 Dockerfile Changes
- .NET 10 base images use Ubuntu (not Debian)
- Use modern GPG key management: `gpg --dearmor` + `signed-by=` in sources list
- Microsoft package repo path: `ubuntu/24.04/prod.list`

## Azure Service Bus Emulator in Aspire

The Service Bus emulator is the most fragile part of the Aspire stack. These learnings were hard-won — do NOT repeat these mistakes.

### NEVER use `WithConfigurationFile()` for emulator topology

`WithConfigurationFile("../../config/servicebus-emulator.json")` causes container mount and networking issues — the emulator container may fail to join the Aspire network, leaving it unable to reach its backing SQL Server. **Always use the fluent API instead:**

```csharp
var serviceBus = builder.AddAzureServiceBus("servicebus");

var topic = serviceBus.AddServiceBusTopic("domain-events");

topic.AddServiceBusSubscription("email-notifications")
    .WithProperties(sub =>
    {
        sub.Rules.Add(new AzureServiceBusRule("MyFilter")
        {
            FilterType = AzureServiceBusFilterType.CorrelationFilter,
            CorrelationFilter = new AzureServiceBusCorrelationFilter
            {
                Properties = { ["EventType"] = "order.created.v1" }
            }
        });
    });

serviceBus.RunAsEmulator(emulator => emulator
    .WithLifetime(ContainerLifetime.Persistent));
```

The static `config/servicebus-emulator.json` file is retained for Docker Compose only.

### NEVER use `WithConfiguration()` with raw JSON for correlation filters

Aspire's `WithConfiguration(doc => { ... })` serializer maps `CorrelationFilter` using its own schema, which produces empty `Properties: {}` in the emulator JSON — even if you set `ApplicationProperties` correctly in the `JsonObject`. The emulator then rejects the filter with: `"At least one system or user property must be set for a correlation filter."` Use the fluent API above instead.

### Memory pressure with the emulator

The Service Bus emulator runs its own backing SQL Server container (`servicebus-mssql`). On machines with limited Docker memory (e.g. 8 GB default WSL2 on a 16 GB machine), two SQL Server instances plus the emulator can OOM (exit code 139). Symptoms:
- Emulator starts, creates topics/subscriptions, then dies with `Out of memory`
- Container shows `Exited (139)` — SIGKILL from OOM killer

If this happens, increase Docker/WSL2 memory via `%USERPROFILE%\.wslconfig`:
```ini
[wsl2]
memory=12GB
```
Then `wsl --shutdown` and restart Docker Desktop. On ARM/Apple Silicon the problem is worse due to Rosetta/QEMU overhead (the emulator image is `linux/amd64` only).

### Debugging emulator startup failures

1. Check `docker ps -a` — look for `Created` (never started) or `Exited (139)` (OOM)
2. Check `docker inspect <container> --format '{{json .NetworkSettings.Networks}}'` — empty `{}` means network wasn't attached
3. Check `docker logs <container>` — look for `"SQL DB Unhealthy"` (network issue) or `"At least one system or user property"` (filter serialization issue)
4. If containers are in bad state, remove them and the Aspire network: `docker rm -f <containers>; docker network rm <aspire-network>`

## Dev Tunnels

Expose your local API to the internet for webhook testing, mobile app development, or sharing with teammates. Uses `Aspire.Hosting.DevTunnels`.

**Prerequisites**: Install Dev Tunnel CLI (https://aka.ms/devtunnels/docs):
```bash
# macOS
brew install --cask devtunnel
# Windows
winget install Microsoft.devtunnel
# Linux
curl -sL https://aka.ms/DevTunnelCliInstall | bash
# Authenticate (one-time)
devtunnel user login
```

**Usage** (opt-in):
```bash
dotnet run --project src/StarterApp.AppHost -- --devtunnel
# Or: ENABLE_DEV_TUNNEL=true dotnet run --project src/StarterApp.AppHost
```

## Local CI with nektos/act

Run GitHub Actions workflows locally using Docker before pushing.

**Prerequisites** (https://nektosact.com/installation/):
```bash
# macOS
brew install act
# Windows
winget install nektos.act
# Linux
curl -s https://raw.githubusercontent.com/nektos/act/master/install.sh | sudo bash
# Requires Docker running
```

**Usage** (`.actrc` pre-configures architecture and image):
```bash
act              # Run CI workflow locally
act -j build     # Run specific job
act -v           # Verbose output
act --list       # List available workflows
```

**Config files**: `.actrc` sets `--container-architecture linux/amd64` and runner image. `.act.env` sets container `PATH` for Node.js in post-steps.

## Post-Session Retrospective

After completing a significant batch of work (bug fixes, feature additions, refactoring), **retrospect and update rules**:

1. Re-read all `.agents/skills/*/SKILL.md` files
2. Identify stale guidance that contradicts the changes just made (e.g., old patterns that were replaced)
3. Update convention class lists and pattern descriptions to match current reality (do NOT hardcode test counts — they go stale immediately)
4. Add new patterns or anti-patterns discovered during the session
5. Update `AGENTS.md` if architectural decisions or project structure changed
6. Mirror equivalent guidance changes into `.claude/skills/**` and `CLAUDE.md`; keep only intentional agent-specific path/name drift
7. Update `docs/ARCHITECTURE_REVIEW.md` to mark resolved issues

This prevents rules from drifting out of sync with the codebase — stale rules actively mislead future work.
