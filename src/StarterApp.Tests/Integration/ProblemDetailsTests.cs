using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using StarterApp.Api.Application.Commands;
using Xunit.Abstractions;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class ProblemDetailsTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ProblemDetailsTests(ApiTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _output.WriteLine("Resetting database for Problem Details test");
            await _fixture.ResetDatabaseAsync();
            _output.WriteLine("Database reset complete");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error during database reset: {ex.GetType().Name}");
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CreateCustomer_WithInvalidEmail_ShouldReturnProblemDetails()
    {
        // Arrange
        var invalidCustomer = new CreateCustomerCommand
        {
            Name = "John Doe",
            Email = "invalid-email-format"
        };

        // Act
        var response = await _fixture.Client.PostAsJsonAsync("/api/customers", invalidCustomer);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Raw response content: {responseContent}");

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
        Assert.Equal("Bad Request", problemDetails.Title);
        Assert.NotNull(problemDetails.Type);
        // Note: Detail may be null for security reasons in the default implementation

        _output.WriteLine($"Problem Details response: {JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions { WriteIndented = true })}");
    }

    [Fact]
    public async Task CreateCustomer_WithEmptyName_ShouldReturnProblemDetails()
    {
        // Arrange
        var invalidCustomer = new CreateCustomerCommand
        {
            Name = "",
            Email = "john.doe@example.com"
        };

        // Act
        var response = await _fixture.Client.PostAsJsonAsync("/api/customers", invalidCustomer);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
        Assert.Equal("Bad Request", problemDetails.Title);
        Assert.NotNull(problemDetails.Type);

        _output.WriteLine($"Problem Details response: {JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions { WriteIndented = true })}");
    }

    [Fact]
    public async Task CreateProduct_WithNegativePrice_ShouldReturnProblemDetails()
    {
        // Arrange
        var invalidProduct = new CreateProductCommand
        {
            Name = "Test Product",
            Description = "Test Description",
            Price = -10.99m,
            Currency = "USD",
            Stock = 100
        };

        // Act
        var response = await _fixture.Client.PostAsJsonAsync("/api/products", invalidProduct);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
        Assert.Equal("Bad Request", problemDetails.Title);
        Assert.NotNull(problemDetails.Type);

        _output.WriteLine($"Problem Details response: {JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions { WriteIndented = true })}");
    }

    [Fact]
    public async Task CreateProduct_WithNegativeStock_ShouldReturnProblemDetails()
    {
        // Arrange
        var invalidProduct = new CreateProductCommand
        {
            Name = "Test Product",
            Description = "Test Description",
            Price = 10.99m,
            Currency = "USD",
            Stock = -5
        };

        // Act
        var response = await _fixture.Client.PostAsJsonAsync("/api/products", invalidProduct);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
        Assert.Equal("Bad Request", problemDetails.Title);
        Assert.NotNull(problemDetails.Type);

        _output.WriteLine($"Problem Details response: {JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions { WriteIndented = true })}");
    }

    [Fact]
    public async Task GetCustomer_WithNonExistentId_ShouldReturnNotFoundProblemDetails()
    {
        // Arrange
        var nonExistentId = 99999;

        // Act
        var response = await _fixture.Client.GetAsync($"/api/customers/{nonExistentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(404, problemDetails.Status);
        Assert.Equal("Not Found", problemDetails.Title);
        Assert.NotNull(problemDetails.Type);

        _output.WriteLine($"Problem Details response: {JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions { WriteIndented = true })}");
    }

    [Fact]
    public async Task GetProduct_WithNonExistentId_ShouldReturnNotFoundProblemDetails()
    {
        // Arrange
        var nonExistentId = 99999;

        // Act
        var response = await _fixture.Client.GetAsync($"/api/products/{nonExistentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(404, problemDetails.Status);
        Assert.Equal("Not Found", problemDetails.Title);
        Assert.NotNull(problemDetails.Type);

        _output.WriteLine($"Problem Details response: {JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions { WriteIndented = true })}");
    }

    [Fact]
    public async Task UpdateCustomer_WithNonExistentId_ShouldReturnNotFoundProblemDetails()
    {
        // Arrange
        var nonExistentId = 99999;
        var updateCommand = new UpdateCustomerCommand
        {
            Id = nonExistentId,
            Name = "Updated Name",
            Email = "updated@example.com"
        };

        // Act
        var response = await _fixture.Client.PutAsJsonAsync($"/api/customers/{nonExistentId}", updateCommand);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(404, problemDetails.Status);
        Assert.Equal("Not Found", problemDetails.Title);
        Assert.NotNull(problemDetails.Type);

        _output.WriteLine($"Problem Details response: {JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions { WriteIndented = true })}");
    }

    [Fact]
    public async Task CreateProduct_WithLongCurrency_ShouldReturnProblemDetails()
    {
        // Arrange
        var invalidProduct = new CreateProductCommand
        {
            Name = "Test Product",
            Description = "Test Description",
            Price = 10.99m,
            Currency = "USDT", // 4 characters, exceeds 3 character limit
            Stock = 100
        };

        // Act
        var response = await _fixture.Client.PostAsJsonAsync("/api/products", invalidProduct);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
        Assert.Equal("Bad Request", problemDetails.Title);
        Assert.NotNull(problemDetails.Type);

        _output.WriteLine($"Problem Details response: {JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions { WriteIndented = true })}");
    }
}
