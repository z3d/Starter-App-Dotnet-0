using System.Text.RegularExpressions;

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

    [Fact]
    public void AstShingles_SimpleHandlersMoreSimilarThanComplex()
    {
        var handlerTypes = _cohort.DiscoverTypes();

        var createProduct = handlerTypes.FirstOrDefault(t => t.Name == "CreateProductCommandHandler");
        var updateProduct = handlerTypes.FirstOrDefault(t => t.Name == "UpdateProductCommandHandler");
        var createOrder = handlerTypes.FirstOrDefault(t => t.Name == "CreateOrderCommandHandler");

        Assert.NotNull(createProduct);
        Assert.NotNull(updateProduct);
        Assert.NotNull(createOrder);

        var shinglesCreateProduct = AstShingleComparer.ComputeShingles(AstShingleComparer.ExtractOpcodeSequence(createProduct));
        var shinglesUpdateProduct = AstShingleComparer.ComputeShingles(AstShingleComparer.ExtractOpcodeSequence(updateProduct));
        var shinglesCreateOrder = AstShingleComparer.ComputeShingles(AstShingleComparer.ExtractOpcodeSequence(createOrder));

        var productPair = AstShingleComparer.JaccardSimilarity(shinglesCreateProduct, shinglesUpdateProduct);
        var productToOrder = AstShingleComparer.JaccardSimilarity(shinglesCreateProduct, shinglesCreateOrder);

        Assert.True(productPair > productToOrder,
            $"Product handler similarity ({productPair:F3}) should exceed CreateProduct/CreateOrder ({productToOrder:F3}).");
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

        var exemplarTypes = handlerTypes.Where(t => _cohort.ExemplarTypeNames.Contains(t.Name)).ToList();
        var shingleScores = AstShingleComparer.ScoreAll(handlerTypes, exemplarTypes);

        Assert.True(structuralScores.Count >= 9);
        Assert.True(shingleScores.Count >= 9);
        Assert.True(structuralScores[0].Distance > 0);

        var maxSim = shingleScores.Max(s => s.AverageSimilarity);
        var minSim = shingleScores.Min(s => s.AverageSimilarity);
        Assert.True(maxSim > minSim);
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
