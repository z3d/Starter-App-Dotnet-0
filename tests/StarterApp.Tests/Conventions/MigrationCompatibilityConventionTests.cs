namespace StarterApp.Tests.Conventions;

public class MigrationCompatibilityConventionTests
{
    // Renaming an embedded script changes its DbUp journal name and re-runs it, so the history before the rule keeps its names.
    private const int FirstEnforcedScript = 10;

    private static readonly string ScriptsDirectory = Path.Combine(TestPaths.RepoRoot, "src", "StarterApp.DbMigrator", "Scripts");

    private static readonly Regex ExpandOrContract = new(@"^\d{4}_(?<kind>Expand|Contract)_[A-Za-z][A-Za-z0-9]*\.sql$");
    private static readonly Regex Completes = new(@"^\s*--\s*completes:\s*(?<script>\d{4}_Expand_[A-Za-z0-9]+\.sql)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex CreatedTable = new(@"\bCREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<table>[\w.]+)", RegexOptions.IgnoreCase);
    private static readonly Regex AddedConstraint = new(@"\bALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?(?<table>[\w.]+)\s+ADD\s+CONSTRAINT\s+\w+\s+(?<kind>UNIQUE|CHECK|PRIMARY\s+KEY)", RegexOptions.IgnoreCase);
    private static readonly Regex UniqueIndex = new(@"\bCREATE\s+UNIQUE\s+INDEX\s+(?:CONCURRENTLY\s+)?(?:IF\s+NOT\s+EXISTS\s+)?\w+\s+ON\s+(?<table>[\w.]+)", RegexOptions.IgnoreCase);
    private static readonly Regex AddedColumn = new(@"\bADD\s+(?:COLUMN\s+)?(?:IF\s+NOT\s+EXISTS\s+)?(?!CONSTRAINT\b)(?<definition>[^,;]+)", RegexOptions.IgnoreCase);

    private static readonly (Regex Pattern, string Statement)[] Forbidden =
    [
        (new Regex(@"\bDROP\s+TABLE\b", RegexOptions.IgnoreCase), "DROP TABLE"),
        (new Regex(@"\bALTER\s+TABLE\b[^;]*\bDROP\s+(?:COLUMN\s+)?(?!CONSTRAINT\b|DEFAULT\b|NOT\s+NULL\b|IDENTITY\b|EXPRESSION\b)\w", RegexOptions.IgnoreCase), "DROP COLUMN"),
        (new Regex(@"\bDROP\s+VIEW\b", RegexOptions.IgnoreCase), "DROP VIEW"),
        (new Regex(@"\bDROP\s+SCHEMA\b", RegexOptions.IgnoreCase), "DROP SCHEMA"),
        (new Regex(@"\bDROP\s+CONSTRAINT\s+(?:IF\s+EXISTS\s+)?pk_", RegexOptions.IgnoreCase), "DROP CONSTRAINT pk_ (a changed primary key)"),
        (new Regex(@"\bALTER\s+TABLE\b[^;]*\bRENAME\b", RegexOptions.IgnoreCase), "ALTER TABLE ... RENAME"),
        (new Regex(@"\bALTER\s+TABLE\b[^;]*\bALTER\s+(?:COLUMN\s+)?\w+\s+(?:SET\s+DATA\s+)?TYPE\b", RegexOptions.IgnoreCase), "ALTER COLUMN ... TYPE"),
        (new Regex(@"\bSET\s+NOT\s+NULL\b", RegexOptions.IgnoreCase), "SET NOT NULL"),
        (new Regex(@"\bTRUNCATE\b", RegexOptions.IgnoreCase), "TRUNCATE"),
        (new Regex(@"\bDELETE\s+FROM\b", RegexOptions.IgnoreCase), "DELETE FROM"),
    ];

    [Fact]
    public void ScriptsUnderTheRule_AreNamedExpandOrContract()
    {
        var failures = EnforcedScripts()
            .Where(file => !ExpandOrContract.IsMatch(Path.GetFileName(file)))
            .Select(file => $"  {Path.GetFileName(file)} must be named NNNN_Expand_Name.sql or NNNN_Contract_Name.sql.")
            .ToList();

        Assert.True(failures.Count == 0,
            "A migration is one phase of an expand/contract change, and says which in its name:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void ScriptsUnderTheRule_SurviveTheBuildStillRunningDuringTheRollout()
    {
        var failures = EnforcedScripts()
            .Where(file => ExpandOrContract.IsMatch(Path.GetFileName(file)))
            .SelectMany(file => Check(Kind(file), File.ReadAllText(file), ExpandScriptExistsBefore(file))
                .Select(failure => $"  {Path.GetFileName(file)}: {failure}"))
            .ToList();

        Assert.True(failures.Count == 0,
            "The migrator runs before the new build starts, so the previous build serves against the new schema until the rollout ends:\n" +
            string.Join("\n", failures));
    }

    [Fact]
    public void TheHistoryBeforeTheRule_PassesAsExpand()
    {
        var failures = Directory.GetFiles(ScriptsDirectory, "*.sql")
            .Where(file => ScriptNumber(file) < FirstEnforcedScript)
            .SelectMany(file => Check("Expand", File.ReadAllText(file), _ => true)
                .Select(failure => $"  {Path.GetFileName(file)}: {failure}"))
            .ToList();

        Assert.True(failures.Count == 0,
            "The deny list flags a shipped script, so it is wider than the rule it enforces:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData("DROP TABLE customers;", "DROP TABLE")]
    [InlineData("ALTER TABLE customers DROP COLUMN phone;", "DROP COLUMN")]
    [InlineData("ALTER TABLE customers DROP phone;", "DROP COLUMN")]
    [InlineData("DROP VIEW customer_summary;", "DROP VIEW")]
    [InlineData("DROP SCHEMA archive CASCADE;", "DROP SCHEMA")]
    [InlineData("ALTER TABLE customers DROP CONSTRAINT pk_customers;", "pk_")]
    [InlineData("ALTER TABLE customers RENAME COLUMN phone TO telephone;", "RENAME")]
    [InlineData("ALTER TABLE customers RENAME TO people;", "RENAME")]
    [InlineData("ALTER TABLE customers ALTER COLUMN phone TYPE varchar(32);", "TYPE")]
    [InlineData("ALTER TABLE customers ALTER phone SET DATA TYPE text;", "TYPE")]
    [InlineData("ALTER TABLE customers ALTER COLUMN phone SET NOT NULL;", "SET NOT NULL")]
    [InlineData("TRUNCATE customers;", "TRUNCATE")]
    [InlineData("DELETE FROM customers WHERE phone IS NULL;", "DELETE FROM")]
    [InlineData("ALTER TABLE customers ADD COLUMN phone varchar(32) NOT NULL;", "NOT NULL without DEFAULT")]
    [InlineData("ALTER TABLE customers ADD phone varchar(32) NOT NULL;", "NOT NULL without DEFAULT")]
    [InlineData("ALTER TABLE customers ADD CONSTRAINT uq_customers_phone UNIQUE (phone);", "UNIQUE")]
    [InlineData("ALTER TABLE customers ADD CONSTRAINT ck_customers_phone CHECK (phone <> '');", "CHECK")]
    [InlineData("CREATE UNIQUE INDEX ix_customers_phone ON customers (phone);", "UNIQUE")]
    public void AnExpandScript_RefusesWhatThePreviousBuildCannotSurvive(string sql, string expected)
    {
        var failures = Check("Expand", sql, _ => true);

        Assert.Contains(failures, failure => failure.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("ALTER TABLE customers ADD COLUMN phone varchar(32) NULL;")]
    [InlineData("ALTER TABLE customers ADD COLUMN phone varchar(32) NOT NULL CONSTRAINT df_customers_phone DEFAULT '';")]
    [InlineData("CREATE TABLE people (id uuid NOT NULL, CONSTRAINT pk_people PRIMARY KEY (id), CONSTRAINT uq_people_id UNIQUE (id));\nCREATE UNIQUE INDEX ix_people_id ON people (id);")]
    [InlineData("CREATE INDEX ix_customers_phone ON customers (phone);")]
    [InlineData("DROP INDEX IF EXISTS ix_customers_phone;")]
    [InlineData("UPDATE customers SET phone = '' WHERE phone IS NULL;")]
    [InlineData("ALTER TABLE customers ALTER COLUMN phone DROP DEFAULT;")]
    [InlineData("ALTER TABLE customers ALTER COLUMN phone DROP NOT NULL;")]
    [InlineData("ALTER TABLE customers DROP CONSTRAINT uq_customers_phone;")]
    [InlineData("-- DROP TABLE customers; is only a comment\nCREATE INDEX ix_customers_phone ON customers (phone);")]
    public void AnExpandScript_AllowsWhatThePreviousBuildSurvives(string sql)
    {
        Assert.Empty(Check("Expand", sql, _ => true));
    }

    [Fact]
    public void AContractScript_NamesTheExpandScriptItCompletes()
    {
        Assert.Contains(Check("Contract", "DROP TABLE customers;", _ => true), failure => failure.Contains("completes:", StringComparison.Ordinal));
        Assert.Contains(Check("Contract", "-- completes: 0010_Expand_Phone.sql\nDROP TABLE customers;", _ => false), failure => failure.Contains("earlier", StringComparison.Ordinal));
        Assert.Empty(Check("Contract", "-- completes: 0010_Expand_Phone.sql\nDROP TABLE customers;", _ => true));
    }

    internal static IReadOnlyList<string> Check(string kind, string sql, Func<string, bool> expandScriptExistsBefore)
    {
        var failures = new List<string>();

        if (kind == "Contract")
        {
            var completes = Completes.Match(sql);
            if (!completes.Success)
                failures.Add("a Contract script names the Expand script it completes in a header comment: '-- completes: 0010_Expand_Name.sql'.");
            else if (!expandScriptExistsBefore(completes.Groups["script"].Value))
                failures.Add($"it completes {completes.Groups["script"].Value}, which is not an earlier script.");

            return failures;
        }

        var body = Regex.Replace(Regex.Replace(sql, @"--[^\r\n]*", ""), @"/\*[\s\S]*?\*/", "").Replace("\"", "");
        var created = CreatedTable.Matches(body).Select(match => match.Groups["table"].Value.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

        failures.AddRange(Forbidden
            .Where(rule => rule.Pattern.IsMatch(body))
            .Select(rule => $"{rule.Statement} is not allowed in an Expand script; it belongs in a Contract script in a later release."));

        failures.AddRange(AddedColumn.Matches(body)
            .Select(match => match.Groups["definition"].Value.Trim())
            .Where(definition => Regex.IsMatch(definition, @"\bNOT\s+NULL\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(definition, @"\bDEFAULT\b", RegexOptions.IgnoreCase))
            .Select(definition => $"ADD COLUMN {definition} is NOT NULL without DEFAULT, so the previous build's inserts fail."));

        failures.AddRange(AddedConstraint.Matches(body)
            .Where(match => !created.Contains(match.Groups["table"].Value.ToLowerInvariant()))
            .Select(match => $"a new {match.Groups["kind"].Value.ToUpperInvariant()} constraint on {match.Groups["table"].Value}, which the previous build still writes."));

        failures.AddRange(UniqueIndex.Matches(body)
            .Where(match => !created.Contains(match.Groups["table"].Value.ToLowerInvariant()))
            .Select(match => $"a new UNIQUE index on {match.Groups["table"].Value}, which the previous build still writes."));

        return failures;
    }

    private static IEnumerable<string> EnforcedScripts() =>
        Directory.GetFiles(ScriptsDirectory, "*.sql").Where(file => ScriptNumber(file) >= FirstEnforcedScript).OrderBy(file => file, StringComparer.Ordinal);

    private static int ScriptNumber(string file) =>
        int.TryParse(Path.GetFileName(file).AsSpan(0, 4), out var number) ? number : 0;

    private static string Kind(string file) =>
        ExpandOrContract.Match(Path.GetFileName(file)).Groups["kind"].Value;

    private static Func<string, bool> ExpandScriptExistsBefore(string contractFile) =>
        expandName => File.Exists(Path.Combine(ScriptsDirectory, expandName)) && ScriptNumber(expandName) < ScriptNumber(contractFile);
}
