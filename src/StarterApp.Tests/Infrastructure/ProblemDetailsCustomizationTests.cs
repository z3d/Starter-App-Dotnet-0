using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StarterApp.Api.Infrastructure;
using StarterApp.Api.Infrastructure.Validation;

namespace StarterApp.Tests.Infrastructure;

public class ProblemDetailsCustomizationTests
{
    [Fact]
    public void AddApiProblemDetails_WithValidationException_ShouldIncludeValidationErrors()
    {
        var services = new ServiceCollection();
        services.AddApiProblemDetails();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ProblemDetailsOptions>>().Value;

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature
        {
            Error = new ValidationException(
            [
                new ValidationError("Email", "Email must be a valid email address"),
                new ValidationError("Name", "Name is required")
            ])
        });

        var problemDetails = new ProblemDetails();
        var context = new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails
        };

        Assert.NotNull(options.CustomizeProblemDetails);

        options.CustomizeProblemDetails(context);

        var errors = Assert.IsType<Dictionary<string, string[]>>(problemDetails.Extensions["errors"]);
        Assert.Equal(["Email must be a valid email address"], errors["Email"]);
        Assert.Equal(["Name is required"], errors["Name"]);
    }
}
