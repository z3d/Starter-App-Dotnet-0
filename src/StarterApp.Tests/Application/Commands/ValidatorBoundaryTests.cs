using StarterApp.Api.Application.Validators;

namespace StarterApp.Tests.Application.Commands;

// Pure validator checks for boundaries the 2026-09-02 review found unmirrored between the
// application validators and the domain guards. No fixture: these never touch the database.
public class ValidatorBoundaryTests
{
    [Fact]
    public void CreateProductCommandValidator_WithAbsentCurrency_ShouldReturnValidationError()
    {
        // Matches UpdateProductCommandValidator: an omitted currency is rejected on both verbs
        // instead of silently defaulting to USD on create.
        var command = new CreateProductCommand { Name = "Widget", Price = 10m, Stock = 1 };

        var errors = new CreateProductCommandValidator().Validate(command).ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(CreateProductCommand.Currency));
    }
}
