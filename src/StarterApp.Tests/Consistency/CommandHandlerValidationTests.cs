
namespace StarterApp.Tests.Consistency;

public class CommandHandlerValidationTests : CohortValidationTestBase<HandlerFingerprint>
{
    private readonly CommandHandlerCohort _cohort = new();

    protected override ICohortDefinition<HandlerFingerprint> Cohort => _cohort;
    protected override string ExemplarDocsFolder => "command-handlers";
    protected override string SourceTreeRelativePath => Path.Combine("src", "StarterApp.Api", "Application");
    protected override string SourceFileGlob => "*Command.cs";
    protected override string ExemplarNameSuffix => "CommandHandler";
    protected override string SourceFileNameToTypeName(string fileNameWithoutExtension) =>
        fileNameWithoutExtension + "Handler";

    protected override void AssertFingerprintIsValid(HandlerFingerprint fp)
    {
        Assert.True(fp.IlByteSize > 0, $"{fp.TypeName} has zero IL byte size");
        Assert.True(fp.ConstructorDependencyCount >= 1,
            $"{fp.TypeName} has no constructor dependencies (should at least have ApplicationDbContext)");
        Assert.True(fp.HasLogger,
            $"{fp.TypeName} should emit Serilog diagnostics so handler execution is observable");
    }

    [Fact]
    public void StructuralFingerprint_CapturesCreateOrderComplexity()
    {
        var fingerprints = ExtractAll();
        var createOrder = fingerprints.FirstOrDefault(f => f.TypeName == "CreateOrderCommandHandler");

        Assert.NotNull(createOrder);
        Assert.True(createOrder.PrivateMethodCount >= 3,
            "CreateOrderCommandHandler should expose its helper-method decomposition.");
        Assert.True(createOrder.EntityLoadCount >= 3,
            "CreateOrderCommandHandler should expose its multi-entity load/reservation shape.");

        var median = fingerprints.Select(f => f.IlByteSize).OrderBy(x => x).ElementAt(fingerprints.Count / 2);
        Assert.True(createOrder.IlByteSize > median * 1.5,
            $"CreateOrderCommandHandler IL byte size ({createOrder.IlByteSize}) should be well above median ({median}).");
    }

    [Fact]
    public void StructuralFingerprint_KnownOutlierExceedsOneSigma()
    {
        var fingerprints = ExtractAll();
        var exemplars = GetExemplars(fingerprints);

        ICohortFingerprint[] all = fingerprints.ToArray<ICohortFingerprint>();
        ICohortFingerprint[] ex = exemplars.ToArray<ICohortFingerprint>();
        var scores = ConsistencyScorer.ScoreAll(all, ex);

        var mean = scores.Average(s => s.Distance);
        var stdDev = Math.Sqrt(scores.Average(s => (s.Distance - mean) * (s.Distance - mean)));
        var threshold = mean + stdDev;

        var createOrder = scores.FirstOrDefault(s => s.TypeName == "CreateOrderCommandHandler");

        Assert.NotNull(createOrder);
        Assert.True(createOrder.Distance > threshold,
            $"CreateOrderCommandHandler distance {createOrder.Distance:F2} must exceed mean+1σ ({threshold:F2}).");
    }


    // Synthetic-fixture extraction tests: prove the fingerprint extractor reads real
    // structure from KNOWN inputs, so the advisory layer cannot silently degrade into
    // extracting zeros for everything (the vacuous-pass failure class). These replace the
    // deleted AST-shingle similarity facts: they pin the machinery, not cohort statistics.
    [Fact]
    public void Extraction_OnLeanSyntheticFixture_ReadsKnownStructure()
    {
        var fingerprint = _cohort.Extract(typeof(LeanSyntheticHandler));

        Assert.Equal(2, fingerprint.ConstructorDependencyCount);
        Assert.True(fingerprint.HasCacheInvalidator);
        Assert.False(fingerprint.HasTryCatch);
        Assert.Equal(0, fingerprint.PrivateMethodCount);
        Assert.Equal(0, fingerprint.EntityLoadCount);
        Assert.True(fingerprint.IlByteSize > 0);
    }

    [Fact]
    public void Extraction_OnBusySyntheticFixture_ReadsKnownStructure()
    {
        var fingerprint = _cohort.Extract(typeof(BusySyntheticHandler));

        Assert.Equal(1, fingerprint.ConstructorDependencyCount);
        Assert.False(fingerprint.HasCacheInvalidator);
        Assert.True(fingerprint.HasTryCatch);
        Assert.Equal(1, fingerprint.PrivateMethodCount);
    }

    private sealed class LeanSyntheticHandler
    {
        private readonly ICacheInvalidator _cacheInvalidator;
        private readonly OwnerPolicyEvaluationTracker _tracker;

        public LeanSyntheticHandler(ICacheInvalidator cacheInvalidator, OwnerPolicyEvaluationTracker tracker)
        {
            _cacheInvalidator = cacheInvalidator;
            _tracker = tracker;
        }

        public Task HandleAsync(CancellationToken cancellationToken)
        {
            _tracker.MarkEvaluated();
            return _cacheInvalidator.InvalidateProductAsync(1, cancellationToken);
        }
    }

    private sealed class BusySyntheticHandler
    {
        private readonly OwnerPolicyEvaluationTracker _tracker;

        public BusySyntheticHandler(OwnerPolicyEvaluationTracker tracker)
        {
            _tracker = tracker;
        }

        public Task HandleAsync(CancellationToken cancellationToken)
        {
            try
            {
                return HelperAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                return Task.CompletedTask;
            }
        }

        private Task HelperAsync(CancellationToken cancellationToken)
        {
            _tracker.MarkEvaluated();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void CombinedScoring_ProducesRankedOutput()
    {
        var handlerTypes = _cohort.DiscoverTypes();
        var fingerprints = ExtractAll();

        var exemplars = GetExemplars(fingerprints);
        ICohortFingerprint[] all = fingerprints.ToArray<ICohortFingerprint>();
        ICohortFingerprint[] ex = exemplars.ToArray<ICohortFingerprint>();
        var structuralScores = ConsistencyScorer.ScoreAll(all, ex);

        Assert.True(structuralScores.Count >= 9);
        Assert.True(structuralScores[0].Distance > 0);
    }

    [Fact]
    public void ExemplarAlignment_DocumentedDependencyCountsMatchCode()
    {
        var readmeText = File.ReadAllText(ExemplarReadmePath);
        var pattern = $@"\*\*`(\w+{ExemplarNameSuffix})\.cs`\*\*.*?(\d+)\s+dependenc";
        var matches = Regex.Matches(readmeText, pattern);

        Assert.True(matches.Count > 0,
            $"Could not parse any dependency counts from {ExemplarReadmePath}.");

        var fingerprints = ExtractAll().ToDictionary(f => f.TypeName);
        var violations = new List<string>();

        foreach (Match match in matches)
        {
            var name = match.Groups[1].Value;
            var documentedDeps = int.Parse(match.Groups[2].Value);

            if (!fingerprints.TryGetValue(name, out var fp))
            {
                violations.Add($"{name}: not found in extracted fingerprints");
                continue;
            }

            if (fp.ConstructorDependencyCount != documentedDeps)
                violations.Add($"{name}: README says {documentedDeps} dependencies, code has {fp.ConstructorDependencyCount}");
        }

        Assert.True(violations.Count == 0,
            $"Exemplar README drift:\n{string.Join("\n", violations)}");
    }

    private List<HandlerFingerprint> ExtractAll() =>
        _cohort.DiscoverTypes().Select(_cohort.Extract).ToList();

    private List<HandlerFingerprint> GetExemplars(List<HandlerFingerprint> all) =>
        all.Where(f => _cohort.ExemplarTypeNames.Contains(f.TypeName)).ToList();
}
