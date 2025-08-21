namespace StarterApp.Tests.Domain;

public class EmailTests
{
    private readonly ITestOutputHelper _output;

    public EmailTests(ITestOutputHelper output)
    {
        _output = output;
        TestLoggerConfiguration.ConfigureTestLogging(output);
    }

    [Fact]
    public void Create_WithValidEmail_ShouldCreateEmail()
    {
        // Arrange
        Log.Information("Testing email creation with valid email");
        var emailValue = "test@example.com";

        // Act
        Log.Debug("Creating email with value {EmailValue}", emailValue);
        var email = Email.Create(emailValue);

        // Assert
        Assert.NotNull(email);
        Assert.Equal(emailValue, email.Value);
        Log.Information("Email object successfully created with expected value");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_WithEmptyOrWhitespace_ShouldThrowArgumentException(string value)
    {
        // Arrange
        Log.Information("Testing email creation with empty or whitespace value: {Value}", value);

        // Act & Assert
        Log.Debug("Attempting to create email with invalid value");
        var exception = Assert.Throws<ArgumentException>(() => Email.Create(value));
        Assert.Contains("cannot be an empty string", exception.Message);
        Log.Information("Exception correctly thrown with message: {Message}", exception.Message);
    }

    [Fact]
    public void Create_WithNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        Log.Information("Testing email creation with null value");

        // Act & Assert
        Log.Debug("Attempting to create email with null value");
        var exception = Assert.Throws<ArgumentNullException>(() => Email.Create(null!));
        Assert.Equal("value", exception.ParamName);
        Log.Information("ArgumentNullException correctly thrown");
    }

    [Fact]
    public void Create_WithTooLongEmail_ShouldThrowArgumentException()
    {
        // Arrange
        Log.Information("Testing email creation with email exceeding max length");
        var longEmail = new string('a', 310) + "@example.com"; // 321 characters

        // Act & Assert
        Log.Debug("Attempting to create email with length {Length}", longEmail.Length);
        var exception = Assert.Throws<ArgumentException>(() => Email.Create(longEmail));
        Assert.Contains($"Email cannot exceed {Email.MaxEmailLength} characters", exception.Message);
        Log.Information("Exception correctly thrown with message: {Message}", exception.Message);
    }

    [Theory]
    [InlineData("invalid-email")]
    [InlineData("@example.com")]
    [InlineData("test@")]
    [InlineData("test.example.com")]
    public void Create_WithInvalidFormat_ShouldThrowArgumentException(string invalidEmail)
    {
        // Arrange
        Log.Information("Testing email creation with invalid format: {Email}", invalidEmail);

        // Act & Assert
        Log.Debug("Attempting to create email with invalid format");
        var exception = Assert.Throws<ArgumentException>(() => Email.Create(invalidEmail));
        Assert.Contains("Invalid email format", exception.Message);
        Log.Information("Exception correctly thrown with message: {Message}", exception.Message);
    }

    [Fact]
    public void Equals_WithSameEmail_ShouldReturnTrue()
    {
        // Arrange
        Log.Information("Testing email equality with same email values");
        var email1 = Email.Create("test@example.com");
        var email2 = Email.Create("test@example.com");

        // Act
        Log.Debug("Comparing emails for equality");
        var areEqual = email1.Equals(email2);

        // Assert
        Assert.True(areEqual);
        Log.Information("Emails correctly determined to be equal");
    }

    [Fact]
    public void Equals_WithDifferentEmail_ShouldReturnFalse()
    {
        // Arrange
        Log.Information("Testing email equality with different email values");
        var email1 = Email.Create("test1@example.com");
        var email2 = Email.Create("test2@example.com");

        // Act
        Log.Debug("Comparing different emails for equality");
        var areEqual = email1.Equals(email2);

        // Assert
        Assert.False(areEqual);
        Log.Information("Emails correctly determined to be different");
    }

    [Fact]
    public void ToString_ShouldReturnEmailValue()
    {
        // Arrange
        Log.Information("Testing email ToString method");
        var emailValue = "test@example.com";
        var email = Email.Create(emailValue);

        // Act
        Log.Debug("Getting string representation of email");
        var result = email.ToString();

        // Assert
        Assert.Equal(emailValue, result);
        Log.Information("ToString method returned expected value: {Value}", result);
    }
}