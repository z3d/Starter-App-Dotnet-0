namespace StarterApp.Tests.Infrastructure;

public class ExceptionStatusCodeMappingTests
{
    [Fact]
    public void ResolveExceptionStatusCode_WithDisabledFeature_ShouldReturnServiceUnavailable()
    {
        var exception = new FeatureDisabledException("some-feature");

        var statusCode = WebApplicationExtensions.ResolveExceptionStatusCode(exception);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusCode);
    }

    [Fact]
    public void ResolveExceptionStatusCode_WithMalformedRequestBody_ShouldReturnBadRequest()
    {
        // Regression: ZAP DAST flagged PUT /api/v1/products/{id} returning HTTP 500 for a
        // malformed JSON body. ASP.NET throws BadHttpRequestException for unreadable bodies;
        // it must map to 400, not fall through to the 500 default.
        var exception = new BadHttpRequestException("Failed to read parameter from the request body as JSON.");

        var statusCode = WebApplicationExtensions.ResolveExceptionStatusCode(exception);

        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
    }

    [Fact]
    public void ResolveExceptionStatusCode_WithMissingEntity_ShouldReturnNotFound()
    {
        var exception = new KeyNotFoundException("Product with ID 10 not found");

        var statusCode = WebApplicationExtensions.ResolveExceptionStatusCode(exception);

        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
    }

    [Fact]
    public void ResolveExceptionStatusCode_WithUnmappedException_ShouldReturnInternalServerError()
    {
        var exception = new InvalidTimeZoneException("unexpected");

        var statusCode = WebApplicationExtensions.ResolveExceptionStatusCode(exception);

        Assert.Equal(StatusCodes.Status500InternalServerError, statusCode);
    }
}
