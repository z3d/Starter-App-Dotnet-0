namespace StarterApp.Tests.Application.DTOs;

public class CustomerDtoTests
{
    [Fact]
    public void CustomerDto_WithValidData_ShouldPassValidation()
    {
        // Arrange
        var customerDto = new CustomerDto
        {
            Name = "John Doe",
            Email = "john.doe@example.com",
            DateCreated = DateTime.UtcNow,
            IsActive = true
        };

        var validationContext = new ValidationContext(customerDto);
        List<ValidationResult> validationResults = [];

        // Act
        var isValid = Validator.TryValidateObject(customerDto, validationContext, validationResults, true);

        // Assert
        Log.Information("Validating CustomerDto with valid data");
        Assert.True(isValid);
        Assert.Empty(validationResults);
    }

    [Fact]
    public void CustomerDto_WithLongName_ShouldFailValidation()
    {
        // Arrange
        var customerDto = new CustomerDto
        {
            Name = new string('A', 101), // 101 characters
            Email = "john.doe@example.com",
            DateCreated = DateTime.UtcNow,
            IsActive = true
        };

        var validationContext = new ValidationContext(customerDto);
        List<ValidationResult> validationResults = [];

        // Act
        Log.Information("Validating CustomerDto with name that exceeds max length");
        var isValid = Validator.TryValidateObject(customerDto, validationContext, validationResults, true);

        // Assert
        Assert.False(isValid);
        Assert.Single(validationResults);
        Assert.Contains("Name", validationResults[0].MemberNames);
        Assert.Contains("cannot exceed 100 characters", validationResults[0].ErrorMessage);
        Log.Information("Validation correctly failed with message: {Message}", validationResults[0].ErrorMessage);
    }

    [Fact]
    public void CustomerDto_WithInvalidEmail_ShouldFailValidation()
    {
        // Arrange
        var customerDto = new CustomerDto
        {
            Name = "John Doe",
            Email = "invalid-email",
            DateCreated = DateTime.UtcNow,
            IsActive = true
        };

        var validationContext = new ValidationContext(customerDto);
        List<ValidationResult> validationResults = [];

        // Act
        Log.Information("Validating CustomerDto with invalid email format");
        var isValid = Validator.TryValidateObject(customerDto, validationContext, validationResults, true);

        // Assert
        Assert.False(isValid);
        Assert.Single(validationResults);
        Assert.Contains("Email", validationResults[0].MemberNames);
        Assert.Contains("Invalid email format", validationResults[0].ErrorMessage);
        Log.Information("Validation correctly failed with message: {Message}", validationResults[0].ErrorMessage);
    }

    [Fact]
    public void CustomerDto_WithLongEmail_ShouldFailValidation()
    {
        // Arrange
        var longEmail = new string('a', 310) + "@example.com"; // 321 characters
        var customerDto = new CustomerDto
        {
            Name = "John Doe",
            Email = longEmail,
            DateCreated = DateTime.UtcNow,
            IsActive = true
        };

        var validationContext = new ValidationContext(customerDto);
        List<ValidationResult> validationResults = [];

        // Act
        Log.Information("Validating CustomerDto with email that exceeds max length");
        var isValid = Validator.TryValidateObject(customerDto, validationContext, validationResults, true);

        // Assert
        Assert.False(isValid);
        Assert.Single(validationResults);
        Assert.Contains("Email", validationResults[0].MemberNames);
        Assert.Contains("cannot exceed 320 characters", validationResults[0].ErrorMessage);
        Log.Information("Validation correctly failed with message: {Message}", validationResults[0].ErrorMessage);
    }

    [Fact]
    public void CustomerDto_WithEmptyName_ShouldFailValidation()
    {
        // Arrange
        var customerDto = new CustomerDto
        {
            Name = "",
            Email = "john.doe@example.com",
            DateCreated = DateTime.UtcNow,
            IsActive = true
        };

        var validationContext = new ValidationContext(customerDto);
        List<ValidationResult> validationResults = [];

        // Act
        Log.Information("Validating CustomerDto with empty name");
        var isValid = Validator.TryValidateObject(customerDto, validationContext, validationResults, true);

        // Assert
        Assert.False(isValid);
        Assert.Single(validationResults);
        Assert.Contains("Name", validationResults[0].MemberNames);
        Log.Information("Validation correctly failed with message: {Message}", validationResults[0].ErrorMessage);
    }

    [Fact]
    public void CustomerDto_WithEmptyEmail_ShouldFailValidation()
    {
        // Arrange
        var customerDto = new CustomerDto
        {
            Name = "John Doe",
            Email = "",
            DateCreated = DateTime.UtcNow,
            IsActive = true
        };

        var validationContext = new ValidationContext(customerDto);
        List<ValidationResult> validationResults = [];

        // Act
        Log.Information("Validating CustomerDto with empty email");
        var isValid = Validator.TryValidateObject(customerDto, validationContext, validationResults, true);

        // Assert
        Assert.False(isValid);
        Assert.Single(validationResults);
        Assert.Contains("Email", validationResults[0].MemberNames);
        Log.Information("Validation correctly failed with message: {Message}", validationResults[0].ErrorMessage);
    }
}
