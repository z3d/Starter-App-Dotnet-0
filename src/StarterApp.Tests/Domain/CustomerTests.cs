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
    public void Create_WithNameExceedingMaxLength_ShouldThrowArgumentException()
    {
        var email = Email.Create("test@example.com");

        var exception = Assert.Throws<ArgumentException>(() =>
            new Customer(new string('c', Customer.MaxNameLength + 1), email));

        Assert.Contains($"Customer name cannot exceed {Customer.MaxNameLength} characters", exception.Message);
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

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void UpdateDetails_WithEmptyOrWhitespaceName_ShouldThrowArgumentException(string invalidName)
    {
        // Arrange
        var customer = new Customer("John Doe", Email.Create("john@example.com"));
        var newEmail = Email.Create("jane@example.com");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => customer.UpdateDetails(invalidName, newEmail));
    }

    [Fact]
    public void UpdateDetails_WithNullEmail_ShouldThrowArgumentNullException()
    {
        // Arrange
        var customer = new Customer("John Doe", Email.Create("john@example.com"));

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => customer.UpdateDetails("Jane Doe", null!));
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

}


