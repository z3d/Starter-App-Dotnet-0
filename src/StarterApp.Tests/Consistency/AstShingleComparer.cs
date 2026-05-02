namespace StarterApp.Tests.Consistency;

/// <summary>
/// Computes AST-like shingle similarity between cohort members.
/// Uses IL opcode sequences as a proxy for AST node types — avoids a Roslyn dependency
/// while still capturing structural skeleton (branching, method calls, exception handling).
/// </summary>
public static class AstShingleComparer
{
    public static IReadOnlyList<byte> ExtractOpcodeSequence(Type type)
    {
        var opcodes = new List<byte>();

        foreach (var method in GetCohortMemberMethods(type))
        {
            var body = method.GetMethodBody();
            var il = body?.GetILAsByteArray();
            if (il == null)
                continue;

            IlInstructionWalker.Walk(il, (opcode, secondByte, _, _) =>
            {
                opcodes.Add(opcode);
                if (opcode == 0xFE)
                    opcodes.Add(secondByte);
            });
        }

        return opcodes;
    }

    public static HashSet<string> ComputeShingles(IReadOnlyList<byte> opcodes, int n = 3)
    {
        var shingles = new HashSet<string>();
        if (opcodes.Count < n)
            return shingles;

        for (var i = 0; i <= opcodes.Count - n; i++)
        {
            var shingle = string.Join("-", opcodes.Skip(i).Take(n).Select(b => b.ToString("X2")));
            shingles.Add(shingle);
        }

        return shingles;
    }

    public static double JaccardSimilarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
            return 1.0;

        var intersection = a.Intersect(b).Count();
        var union = a.Union(b).Count();

        return union == 0 ? 1.0 : (double)intersection / union;
    }

    public static double SimilarityToExemplars(Type type, IReadOnlyList<Type> exemplarTypes, int shingleSize = 3)
    {
        var memberShingles = ComputeShingles(ExtractOpcodeSequence(type), shingleSize);

        if (exemplarTypes.Count == 0)
            return 0.0;

        var totalSimilarity = 0.0;
        foreach (var exemplar in exemplarTypes)
        {
            var exemplarShingles = ComputeShingles(ExtractOpcodeSequence(exemplar), shingleSize);
            totalSimilarity += JaccardSimilarity(memberShingles, exemplarShingles);
        }

        return totalSimilarity / exemplarTypes.Count;
    }

    public static IReadOnlyList<ShingleScore> ScoreAll(
        IReadOnlyList<Type> allMembers,
        IReadOnlyList<Type> exemplars,
        int shingleSize = 3)
    {
        var exemplarShingles = exemplars
            .Select(e => ComputeShingles(ExtractOpcodeSequence(e), shingleSize))
            .ToList();

        return allMembers
            .Select(type =>
            {
                var memberShingles = ComputeShingles(ExtractOpcodeSequence(type), shingleSize);
                var similarities = exemplarShingles.Select(es => JaccardSimilarity(memberShingles, es)).ToList();
                var avgSimilarity = similarities.Count > 0 ? similarities.Average() : 0.0;

                return new ShingleScore(type.Name, avgSimilarity, memberShingles.Count);
            })
            .OrderBy(s => s.AverageSimilarity)
            .ToList();
    }

    /// <summary>
    /// Delegates to IlInstructionWalker.GetOperandSize for testing.
    /// </summary>
    public static int GetOperandSize(byte opcode, byte secondByte = 0) =>
        IlInstructionWalker.GetOperandSize(opcode, secondByte);

    private static IEnumerable<MethodInfo> GetCohortMemberMethods(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.DeclaredOnly;

        return type.GetMethods(flags)
            .Concat(type.GetNestedTypes(BindingFlags.NonPublic)
                .SelectMany(nested => nested.GetMethods(flags)));
    }
}

public record ShingleScore(string TypeName, double AverageSimilarity, int ShingleCount);
