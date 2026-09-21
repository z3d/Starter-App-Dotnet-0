namespace StarterApp.Tests.Application.Commands;

public class DeleteProductCommandTests
{
    [Fact]
    public void DeleteProductCommand_PropertiesTest()
    {
        // Arrange & Act
        var command = new DeleteProductCommand(42);

        // Assert
        Assert.Equal(42, command.Id);
    }
}
