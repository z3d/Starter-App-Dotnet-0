namespace StarterApp.Tests.Consistency;

public class AstShingleComparerTests
{
    [Fact]
    public void JaccardSimilarity_IdenticalSets_ReturnsOne()
    {
        var set = new HashSet<string> { "a", "b", "c" };
        Assert.Equal(1.0, AstShingleComparer.JaccardSimilarity(set, set));
    }

    [Fact]
    public void JaccardSimilarity_DisjointSets_ReturnsZero()
    {
        var a = new HashSet<string> { "a", "b" };
        var b = new HashSet<string> { "c", "d" };
        Assert.Equal(0.0, AstShingleComparer.JaccardSimilarity(a, b));
    }

    [Fact]
    public void JaccardSimilarity_PartialOverlap_ReturnsCorrectValue()
    {
        var a = new HashSet<string> { "a", "b", "c" };
        var b = new HashSet<string> { "b", "c", "d" };
        Assert.Equal(0.5, AstShingleComparer.JaccardSimilarity(a, b));
    }

    [Fact]
    public void ComputeShingles_ShortSequence_ReturnsEmpty()
    {
        var opcodes = new byte[] { 0x00, 0x01 };
        var shingles = AstShingleComparer.ComputeShingles(opcodes, n: 3);
        Assert.Empty(shingles);
    }

    [Fact]
    public void ComputeShingles_LongerSequence_ReturnsCorrectCount()
    {
        var opcodes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        var shingles = AstShingleComparer.ComputeShingles(opcodes, n: 3);
        Assert.Equal(3, shingles.Count);
    }

    [Theory]
    [InlineData(0x27, 0, 4)]
    [InlineData(0x28, 0, 4)]
    [InlineData(0x2B, 0, 1)]
    [InlineData(0x38, 0, 4)]
    [InlineData(0x20, 0, 4)]
    [InlineData(0x1F, 0, 1)]
    [InlineData(0x72, 0, 4)]
    [InlineData(0x6F, 0, 4)]
    [InlineData(0x73, 0, 4)]
    [InlineData(0xFE, 0x06, 4)]
    [InlineData(0xFE, 0x01, 0)]
    [InlineData(0xFE, 0x09, 2)]
    [InlineData(0xFE, 0x0B, 2)]
    public void GetOperandSize_MatchesRuntimeMetadata(byte opcode, byte secondByte, int expectedSize)
    {
        var size = AstShingleComparer.GetOperandSize(opcode, secondByte);
        Assert.Equal(expectedSize, size);
    }

    [Fact]
    public void ExtractOpcodeSequence_RealHandlerType_ReturnsNonEmpty()
    {
        var cohort = new CommandHandlerCohort();
        var handlerTypes = cohort.DiscoverTypes();
        Assert.NotEmpty(handlerTypes);

        var opcodes = AstShingleComparer.ExtractOpcodeSequence(handlerTypes[0]);
        Assert.NotEmpty(opcodes);
    }

    [Fact]
    public void SimilarHandlers_HaveHigherJaccardThanDissimilar()
    {
        var cohort = new CommandHandlerCohort();
        var handlerTypes = cohort.DiscoverTypes();

        var createProduct = handlerTypes.FirstOrDefault(t => t.Name == "CreateProductCommandHandler");
        var updateProduct = handlerTypes.FirstOrDefault(t => t.Name == "UpdateProductCommandHandler");
        var createOrder = handlerTypes.FirstOrDefault(t => t.Name == "CreateOrderCommandHandler");

        Assert.NotNull(createProduct);
        Assert.NotNull(updateProduct);
        Assert.NotNull(createOrder);

        var productCreate = AstShingleComparer.ComputeShingles(AstShingleComparer.ExtractOpcodeSequence(createProduct));
        var productUpdate = AstShingleComparer.ComputeShingles(AstShingleComparer.ExtractOpcodeSequence(updateProduct));
        var orderCreate = AstShingleComparer.ComputeShingles(AstShingleComparer.ExtractOpcodeSequence(createOrder));

        var productPair = AstShingleComparer.JaccardSimilarity(productCreate, productUpdate);
        var productToOrder = AstShingleComparer.JaccardSimilarity(productCreate, orderCreate);

        Assert.True(productPair > productToOrder,
            $"Expected product pair ({productPair:F3}) > product/order ({productToOrder:F3})");
    }
}
