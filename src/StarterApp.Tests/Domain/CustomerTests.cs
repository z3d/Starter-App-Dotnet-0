namespace StarterApp.Tests.Domain;

public class CustomerTests
{
    [Fact]
    public void Create_WithValidInputs_ShouldCreateCustomer()
    {
        // Arrange
        var name = "John Doe";
        var email = Email.Create("john.doe@example.com");

        // Act
        var customer = new Customer(name, email);

        // Assert
        Assert.NotNull(customer);
        Assert.Equal(name, customer.Name);
        Assert.Equal(email, customer.Email);
        Assert.True(customer.IsActive);
        Assert.True(customer.DateCreated <= DateTime.UtcNow);
        Assert.True(customer.DateCreated > DateTime.UtcNow.AddMinutes(-1));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_WithEmptyOrWhitespaceName_ShouldThrowArgumentException(string invalidName)
    {
        // Arrange
        var email = Email.Create("test@example.com");

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => new Customer(invalidName, email));
        Assert.Contains("cannot be an empty string", exception.Message);
    }

    [Fact]
    public void Create_WithNullName_ShouldThrowArgumentNullException()
    {
        // Arrange
        var email = Email.Create("test@example.com");

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new Customer(null!, email));
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Create_WithNullEmail_ShouldThrowArgumentNullException()
    {
        // Arrange
        var name = "John Doe";
        Email? email = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new Customer(name, email!));
        Assert.Equal("email", exception.ParamName);
    }

    [Fact]
    public void UpdateDetails_WithValidParameters_ShouldUpdateCustomer()
    {
        // Arrange
        var customer = new Customer("John Doe", Email.Create("john@example.com"));
        var newName = "Jane Doe";
        var newEmail = Email.Create("jane@example.com");

        // Act
        customer.UpdateDetails(newName, newEmail);

        // Assert
        Assert.Equal(newName, customer.Name);
        Assert.Equal(newEmail, customer.Email);
    }

    [Fact]
    public void UpdateDetails_WithEmptyName_ShouldNotUpdate()
    {
        // Arrange
        var originalName = "John Doe";
        var customer = new Customer(originalName, Email.Create("john@example.com"));
        var newEmail = Email.Create("jane@example.com");

        // Act
        customer.UpdateDetails("", newEmail);

        // Assert
        Assert.Equal(originalName, customer.Name);
        Assert.Equal(newEmail, customer.Email);
    }

    [Fact]
    public void UpdateDetails_WithNullEmail_ShouldNotUpdate()
    {
        // Arrange
        var originalEmail = Email.Create("john@example.com");
        var customer = new Customer("John Doe", originalEmail);
        var newName = "Jane Doe";

        // Act
        customer.UpdateDetails(newName, null!);

        // Assert
        Assert.Equal(newName, customer.Name);
        Assert.Equal(originalEmail, customer.Email);
    }

    [Fact]
    public void Activate_ShouldSetIsActiveToTrue()
    {
        // Arrange
        var customer = new Customer("John Doe", Email.Create("john@example.com"));
        customer.Deactivate();

        // Act
        customer.Activate();

        // Assert
        Assert.True(customer.IsActive);
    }

    [Fact]
    public void Deactivate_ShouldSetIsActiveToFalse()
    {
        // Arrange
        var customer = new Customer("John Doe", Email.Create("john@example.com"));

        // Act
        customer.Deactivate();

        // Assert
        Assert.False(customer.IsActive);
    }

    [Fact]
    public void SetId_ShouldSetIdProperty()
    {
        // Arrange
        var customer = new Customer("John Doe", Email.Create("john@example.com"));
        var expectedId = 123;

        // Act
        customer.SetId(expectedId);

        // Assert
        Assert.Equal(expectedId, customer.Id);
    }
}



