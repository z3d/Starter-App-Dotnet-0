using System.Diagnostics;

namespace StarterApp.Tests.Hooks;

public sealed class HookScriptTests : IDisposable
{
    private static readonly string HooksDirectory = Path.Combine(TestPaths.RepoRoot, ".claude", "hooks");
    private static readonly string GitHooksDirectory = Path.Combine(TestPaths.RepoRoot, ".githooks");

    private readonly string _scratch = Directory.CreateTempSubdirectory("hook-tests-").FullName;
    private readonly string _fakeBin;
    private readonly string _toolLog;

    public HookScriptTests()
    {
        _fakeBin = Directory.CreateDirectory(Path.Combine(_scratch, "bin")).FullName;
        _toolLog = Path.Combine(_scratch, "tools.log");
        foreach (var tool in new[] { "dotnet", "npm" })
            WriteExecutable(Path.Combine(_fakeBin, tool), $"#!/usr/bin/env bash\necho \"{tool} $*\" >> '{_toolLog}'\nexit 0\n");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData("pkill -f dotnet")]
    [InlineData("ls; pkill -f dotnet")]
    [InlineData("sudo killall node")]
    public void Guard_RefusesKillingByPattern(string command)
    {
        Assert.Equal(2, RunGuard(command).ExitCode);
    }

    [Theory]
    [InlineData("git commit -m \"The guard no longer refuses ; pkill in a message\"")]
    [InlineData("git commit -m 'Explain ; killall and ; podman machine stop'")]
    [InlineData("git commit -F - <<'EOF'\nSubject\n\npkill -f dotnet is refused by the guard\nEOF")]
    public void Guard_AllowsACommitMessageThatNamesARefusedCommand(string command)
    {
        Assert.Equal(0, RunGuard(command).ExitCode);
    }

    [Theory]
    [InlineData("pnpm --dir web exec playwright test")]
    [InlineData("npm run build")]
    [InlineData("cd web && npm run e2e")]
    [InlineData("npm ci --no-audit")]
    [InlineData("pnpm install")]
    [InlineData("npx playwright install chromium")]
    [InlineData("dotnet test")]
    public void Guard_ChecksFreeDiskBeforeHeavyWork(string command)
    {
        var result = RunGuard(command, ("AGENT_MIN_FREE_GB", "1000000"));

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("heavy work", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("npm run lint")]
    [InlineData("git status")]
    [InlineData("pnpm --version")]
    public void Guard_LetsLightWorkThroughOnAFullDisk(string command)
    {
        Assert.Equal(0, RunGuard(command, ("AGENT_MIN_FREE_GB", "1000000")).ExitCode);
    }

    [Fact]
    public void PreCommit_NoVerify_PassesADocumentationOnlyTree()
    {
        var repo = NewRepository();
        File.WriteAllText(Path.Combine(repo, "docs", "notes.md"), "changed\n");
        File.WriteAllText(Path.Combine(repo, "README.md"), "changed\n");

        var result = RunPreCommit(repo, "git commit --no-verify -m 'Docs'");

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(_toolLog), "a documentation-only --no-verify commit runs no build");
    }

    [Theory]
    [InlineData("src/Code.cs", true)]
    [InlineData("src/Code.cs", false)]
    [InlineData("src/Untracked.cs", false)]
    public void PreCommit_NoVerify_RefusesATreeThatChangesCode(string path, bool staged)
    {
        var repo = NewRepository();
        File.WriteAllText(Path.Combine(repo, "docs", "notes.md"), "changed\n");
        File.WriteAllText(Path.Combine(repo, path), "class Changed {}\n");
        if (staged)
            Git(repo, "add", "-A");

        var result = RunPreCommit(repo, "git add -A && git commit --no-verify -m 'Sneak'");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("documentation-only", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void PreCommit_RefusesPushingPastThePrePushGate()
    {
        var result = RunPreCommit(NewRepository(), "git push --no-verify origin HEAD:main");

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void PreCommit_DoesNotReadACommitMessageAsAPush()
    {
        var result = RunPreCommit(NewRepository(), "git commit -m 'Refuse git push --no-verify'");

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void PrePush_SkipsTheGateForADocumentationOnlyPush()
    {
        var repo = NewRepository();
        var before = Git(repo, "rev-parse", "HEAD").Trim();
        File.WriteAllText(Path.Combine(repo, "docs", "notes.md"), "changed\n");
        Commit(repo, "Docs");

        var result = RunPrePush(repo, before);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(_toolLog), "a documentation-only push runs no build");
    }

    [Fact]
    public void PrePush_RunsTheFullGateForACodeChange()
    {
        var repo = NewRepository();
        var before = Git(repo, "rev-parse", "HEAD").Trim();
        File.WriteAllText(Path.Combine(repo, "src", "Code.cs"), "class Changed {}\n");
        Commit(repo, "Code");

        var result = RunPrePush(repo, before);

        Assert.Equal(0, result.ExitCode);
        var log = File.ReadAllText(_toolLog);
        Assert.Contains("dotnet format --verify-no-changes", log, StringComparison.Ordinal);
        Assert.Contains("dotnet build", log, StringComparison.Ordinal);
        Assert.Contains("dotnet test --no-build", log, StringComparison.Ordinal);
        Assert.Contains("npm run typecheck", log, StringComparison.Ordinal);
        Assert.Contains("npm test", log, StringComparison.Ordinal);
    }

    [Fact]
    public void PrePush_FailsWhenTheGateFails()
    {
        WriteExecutable(Path.Combine(_fakeBin, "dotnet"), "#!/usr/bin/env bash\n[ \"$1\" = test ] && exit 1\nexit 0\n");
        var repo = NewRepository();
        var before = Git(repo, "rev-parse", "HEAD").Trim();
        File.WriteAllText(Path.Combine(repo, "src", "Code.cs"), "class Changed {}\n");
        Commit(repo, "Code");

        Assert.NotEqual(0, RunPrePush(repo, before).ExitCode);
    }

    [Fact]
    public void CiStatus_AttributesEachFailedRunToItsOwnCause()
    {
        var repo = NewRepository();
        Git(repo, "remote", "add", "origin", "https://github.com/example/repo.git");
        var runs = JsonSerializer.Serialize(new[]
        {
            new { databaseId = 2, workflowName = "CI Build", status = "completed", conclusion = "failure", createdAt = "2026-10-23T02:00:00Z", url = "https://example/2" },
            new { databaseId = 1, workflowName = "CodeQL", status = "completed", conclusion = "failure", createdAt = "2026-10-23T01:00:00Z", url = "https://example/1" },
        });
        File.WriteAllText(Path.Combine(_scratch, "runs.json"), runs);
        WriteExecutable(Path.Combine(_fakeBin, "gh"), $$"""
            #!/usr/bin/env bash
            case "$1 $2" in
              "run list") cat '{{Path.Combine(_scratch, "runs.json")}}' ;;
              "run view")
                if [ "$3" = 1 ]; then echo "X The job was not started because recent account payments have failed or your spending limit needs to be increased."
                else echo "X Unit tests failed"; fi ;;
            esac
            """);

        var result = Run("bash", [Path.Combine(HooksDirectory, "ci-status.sh")], repo, "{}", ("CLAUDE_PROJECT_DIR", repo));

        Assert.Equal(0, result.ExitCode);
        var message = JsonDocument.Parse(result.Output).RootElement.GetProperty("systemMessage").GetString()!;
        var lines = message.Split('\n');
        Assert.Contains(lines, line => line.Contains("CodeQL: GitHub never started the jobs", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("CI Build: failure on 2026-10-23", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("CI Build", StringComparison.Ordinal) && line.Contains("never started", StringComparison.Ordinal));
    }

    private ProcessResult RunGuard(string command, params (string Name, string Value)[] environment)
    {
        var payload = JsonSerializer.Serialize(new { tool_input = new { command } });
        return Run("bash", [Path.Combine(HooksDirectory, "shared-machine-guard.sh")], _scratch, payload,
            [("CLAUDE_PROJECT_DIR", _scratch), .. environment]);
    }

    private ProcessResult RunPreCommit(string repo, string command)
    {
        var payload = JsonSerializer.Serialize(new { tool_input = new { command }, cwd = repo });
        return Run("bash", [Path.Combine(HooksDirectory, "pre-commit.sh")], repo, payload);
    }

    private ProcessResult RunPrePush(string repo, string remoteSha)
    {
        var head = Git(repo, "rev-parse", "HEAD").Trim();
        return Run("bash", [Path.Combine(GitHooksDirectory, "pre-push"), "origin", "https://example/repo.git"], repo,
            $"refs/heads/main {head} refs/heads/main {remoteSha}\n");
    }

    private string NewRepository()
    {
        var repo = Directory.CreateDirectory(Path.Combine(_scratch, "repo-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        Git(repo, "init", "-q", "-b", "main");
        Directory.CreateDirectory(Path.Combine(repo, "docs"));
        Directory.CreateDirectory(Path.Combine(repo, "src"));
        Directory.CreateDirectory(Path.Combine(repo, "web", "node_modules"));
        File.WriteAllText(Path.Combine(repo, ".gitignore"), "node_modules/\n");
        File.WriteAllText(Path.Combine(repo, "docs", "notes.md"), "notes\n");
        File.WriteAllText(Path.Combine(repo, "README.md"), "readme\n");
        File.WriteAllText(Path.Combine(repo, "src", "Code.cs"), "class Code {}\n");
        File.WriteAllText(Path.Combine(repo, "web", "package.json"), "{}\n");
        Commit(repo, "Initial");
        return repo;
    }

    private void Commit(string repo, string message)
    {
        Git(repo, "add", "-A");
        Git(repo, "-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "core.hooksPath=/dev/null", "commit", "-q", "-m", message);
    }

    private string Git(string repo, params string[] args)
    {
        var result = Run("git", args, repo, string.Empty);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', args)} failed: {result.Error}");
        return result.Output;
    }

    private static void WriteExecutable(string path, string content)
    {
        File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private ProcessResult Run(string fileName, IEnumerable<string> args, string workingDirectory, string stdin, params (string Name, string Value)[] environment)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        foreach (var inherited in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.Ordinal)).ToList())
            start.Environment.Remove(inherited);
        start.Environment["PATH"] = $"{_fakeBin}:{Environment.GetEnvironmentVariable("PATH")}";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["HOME"] = _scratch;
        start.Environment.Remove("CLAUDECODE");
        foreach (var (name, value) in environment)
            start.Environment[name] = value;

        using var process = Process.Start(start)!;
        process.StandardInput.Write(stdin);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} {string.Join(' ', args)} did not finish");
        }

        return new ProcessResult(process.ExitCode, output.Result, error.Result);
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
