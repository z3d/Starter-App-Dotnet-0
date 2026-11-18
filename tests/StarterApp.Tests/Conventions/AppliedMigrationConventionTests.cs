namespace StarterApp.Tests.Conventions;

public class AppliedMigrationConventionTests
{
    private static readonly string ScriptsDirectory = Path.Combine(TestPaths.RepoRoot, "src", "StarterApp.DbMigrator", "Scripts");
    private static readonly string LedgerPath = Path.Combine(ScriptsDirectory, "applied.sha256");

    [Fact]
    public void EveryScript_IsRecordedAndUnchanged()
    {
        var ledger = Ledger();
        var failures = new List<string>();

        foreach (var file in Directory.GetFiles(ScriptsDirectory, "*.sql").OrderBy(file => file, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            var checksum = Checksum(file);

            if (!ledger.TryGetValue(name, out var recorded))
                failures.Add($"  {name} is not recorded. Add this line to applied.sha256:\n    {checksum}  {name}");
            else if (!string.Equals(recorded, checksum, StringComparison.OrdinalIgnoreCase))
                failures.Add($"  {name} has changed since it was recorded. A database that already ran it keeps the old version; put the change in a new script.");
        }

        Assert.True(failures.Count == 0,
            "DbUp journals a script by name and never runs it again, so a script is fixed once it is recorded:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void EveryRecordedScript_StillExists()
    {
        var missing = Ledger().Keys
            .Where(name => !File.Exists(Path.Combine(ScriptsDirectory, name)))
            .Select(name => $"  {name}")
            .ToList();

        Assert.True(missing.Count == 0,
            "A recorded script was renamed or deleted. A rename runs it again under the new name, and a deleted one stays applied in every database that ran it:\n" +
            string.Join("\n", missing));
    }

    [Fact]
    public void TheLedger_HasOneWellFormedLinePerScript()
    {
        var failures = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in Lines())
        {
            var parts = line.Split("  ", 2);
            if (parts.Length != 2 || parts[0].Length != 64 || !parts[0].All(Uri.IsHexDigit) || !parts[1].EndsWith(".sql", StringComparison.Ordinal))
                failures.Add($"  not '<sha256>  <script>.sql': {line}");
            else if (!seen.Add(parts[1]))
                failures.Add($"  {parts[1]} is recorded twice");
        }

        Assert.True(failures.Count == 0, "applied.sha256:\n" + string.Join("\n", failures));
    }

    private static Dictionary<string, string> Ledger() =>
        Lines()
            .Select(line => line.Split("  ", 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[1], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First()[0], StringComparer.Ordinal);

    private static IEnumerable<string> Lines() =>
        File.ReadAllLines(LedgerPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'));

    // Line endings and a byte-order mark depend on the checkout, not the script, so neither counts as a change.
    private static string Checksum(string file)
    {
        var text = File.ReadAllText(file).TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }
}
