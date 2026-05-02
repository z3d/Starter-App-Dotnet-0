namespace StarterApp.Tests.Consistency;

/// <summary>
/// A deterministic, local code embedder that produces vectors from IL token content.
///
/// Builds a bag-of-tokens vector from method names, string literals, type references,
/// and field accesses resolved from IL metadata. Walks IL on instruction boundaries
/// using IlInstructionWalker so operand bytes are never mistaken for opcodes.
///
/// Limitations:
/// - No deep semantic understanding. Synonyms, abstractions, and intent are invisible.
/// - Vocabulary is bounded by the tokens actually present in the IL.
/// - For production use, replace with a real code embedding model via ICodeEmbedder.
/// </summary>
public class SourceTokenEmbedder : ICodeEmbedder
{
    private readonly int _buckets;

    public SourceTokenEmbedder(int buckets = 128)
    {
        _buckets = buckets;
    }

    public int Dimensions => _buckets;

    public double[] Embed(Type type)
    {
        var vector = new double[_buckets];
        var tokens = ExtractSemanticTokens(type);

        foreach (var token in tokens)
        {
            var bucket = StableHash(token) % _buckets;
            vector[bucket] += 1.0;
        }

        var norm = Math.Sqrt(vector.Sum(v => v * v));
        if (norm > 1e-10)
        {
            for (var i = 0; i < _buckets; i++)
                vector[i] /= norm;
        }

        return vector;
    }

    /// <summary>
    /// Extracts semantic tokens by walking IL on instruction boundaries.
    /// Each opcode is visited exactly once; operand bytes are skipped, not misread.
    ///
    /// Resolver selection per opcode:
    /// - ldstr (0x72): ResolveString — string literals
    /// - call (0x28), callvirt (0x6F): ResolveMethod — method/type references
    /// - newobj (0x73): ResolveMethod — constructor, yields declaring type
    /// - castclass (0x74), isinst (0x75): ResolveType — type tokens
    /// - ldfld (0x7B), stfld (0x7D), ldsfld (0x7E): ResolveField — field names
    /// - ldtoken (0xD0): ResolveMember — generic metadata token
    /// </summary>
    internal static List<string> ExtractSemanticTokens(Type type)
    {
        var tokens = new List<string>();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.DeclaredOnly;

        var methods = type.GetMethods(flags)
            .Concat(type.GetNestedTypes(BindingFlags.NonPublic)
                .SelectMany(nested => nested.GetMethods(flags)));

        foreach (var method in methods)
        {
            var body = method.GetMethodBody();
            var il = body?.GetILAsByteArray();
            if (il == null || il.Length < 5)
                continue;

            var module = method.Module;

            IlInstructionWalker.Walk(il, (opcode, _, operandStart, operandSize) =>
            {
                if (operandSize < 4 || operandStart + 3 >= il.Length)
                    return;

                var metadataToken = BitConverter.ToInt32(il, operandStart);

                switch (opcode)
                {
                    // String literals
                    case 0x72: // ldstr
                        try
                        {
                            var s = module.ResolveString(metadataToken);
                            foreach (var word in SplitIntoWords(s))
                                tokens.Add("str:" + word.ToLowerInvariant());
                        }
                        catch { /* invalid token */ }
                        break;

                    // Method calls — resolve as method, extract method name + declaring type
                    case 0x28: // call
                    case 0x6F: // callvirt
                        try
                        {
                            var resolved = module.ResolveMethod(metadataToken);
                            if (resolved != null)
                            {
                                tokens.Add("method:" + resolved.Name);
                                if (resolved.DeclaringType != null)
                                    tokens.Add("type:" + resolved.DeclaringType.Name);
                            }
                        }
                        catch { /* invalid token */ }
                        break;

                    // newobj — carries a method token (the constructor)
                    case 0x73: // newobj
                        try
                        {
                            var ctor = module.ResolveMethod(metadataToken);
                            if (ctor?.DeclaringType != null)
                                tokens.Add("newtype:" + ctor.DeclaringType.Name);
                        }
                        catch { /* invalid token */ }
                        break;

                    // castclass / isinst — carry a TYPE token, not a method token
                    case 0x74: // castclass
                    case 0x75: // isinst
                        try
                        {
                            var resolvedType = module.ResolveType(metadataToken);
                            tokens.Add("casttype:" + resolvedType.Name);
                        }
                        catch { /* invalid token */ }
                        break;

                    // Field access
                    case 0x7B: // ldfld
                    case 0x7D: // stfld
                    case 0x7E: // ldsfld
                    case 0x7F: // ldsflda
                    case 0x80: // stsfld
                        try
                        {
                            var field = module.ResolveField(metadataToken);
                            if (field != null)
                                tokens.Add("field:" + field.Name);
                        }
                        catch { /* invalid token */ }
                        break;

                    // Generic metadata token
                    case 0xD0: // ldtoken
                        try
                        {
                            var member = module.ResolveMember(metadataToken);
                            if (member != null)
                                tokens.Add("token:" + member.Name);
                        }
                        catch { /* invalid token */ }
                        break;
                }
            });
        }

        return tokens;
    }

    private static IEnumerable<string> SplitIntoWords(string s)
    {
        return s.Split([' ', '.', ',', '=', '{', '}', '(', ')', ':', ';', '\n', '\r', '\t', '/', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3);
    }

    private static uint StableHash(string s)
    {
        uint hash = 2166136261;
        foreach (var c in s)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return hash;
    }
}
