namespace StarterApp.Tests.Application.Commands;

public class DeleteCustomerCommandTests
{
    [Fact]
    public void DeleteCustomerCommand_PropertiesTest()
    {
        // Arrange & Act
        var command = new DeleteCustomerCommand { Id = 42 };

        // Assert
        Assert.Equal(42, command.Id);
    }
}
