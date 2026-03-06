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

- **Connection strings**: Must resolve via fallback chain (`database` → `DockerLearning` → `sqlserver` → `DefaultConnection`) so both Aspire-injected and Docker-compose-provided values work
- **Health checks**: `app.MapHealthChecks("/health")` must be mapped unconditionally — Aspire's `MapDefaultEndpoints()` only maps in Development environment, which is insufficient for Docker healthchecks
- **Service registration**: All DI registrations must work without Aspire service discovery present — use conditional checks or fallback defaults where Aspire provides configuration

### .NET 10 Dockerfile Changes
- .NET 10 base images use Ubuntu (not Debian)
- Use modern GPG key management: `gpg --dearmor` + `signed-by=` in sources list
- Microsoft package repo path: `ubuntu/24.04/prod.list`

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

1. Re-read all `.claude/rules/*.md` files
2. Identify stale guidance that contradicts the changes just made (e.g., old patterns that were replaced)
3. Update test counts, convention lists, and pattern descriptions to match current reality
4. Add new patterns or anti-patterns discovered during the session
5. Update `CLAUDE.md` if architectural decisions or project structure changed
6. Update `docs/ARCHITECTURE_REVIEW.md` to mark resolved issues

This prevents rules from drifting out of sync with the codebase — stale rules actively mislead future work.
