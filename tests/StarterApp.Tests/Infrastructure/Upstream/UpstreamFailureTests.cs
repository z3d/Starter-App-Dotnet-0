using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using StarterApp.Api.Infrastructure.Upstream;

namespace StarterApp.Tests.Infrastructure.Upstream;

public class UpstreamFailureTests
{
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.BadGateway, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    [InlineData(HttpStatusCode.RequestTimeout, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    public void EnsureUpstreamSuccess_ClassifiesWhetherTheFailureMayClear(HttpStatusCode status, bool permanent)
    {
        using var response = new HttpResponseMessage(status);

        var exception = Assert.Throws<UpstreamServiceException>(() => response.EnsureUpstreamSuccess("rates"));

        Assert.Equal(permanent, exception.IsPermanent);
        Assert.Equal("rates", exception.Service);
    }

    [Fact]
    public void EnsureUpstreamSuccess_PassesASuccessThrough()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        Assert.Same(response, response.EnsureUpstreamSuccess("rates"));
    }

    [Theory]
    [InlineData("Production", "An outside service is not answering. Try again shortly.")]
    [InlineData("Testing", "An outside service is not answering. Try again shortly.")]
    [InlineData("Development", "The outside service 'rates' could not be reached after its retries.")]
    public void ProblemDetail_NamesTheServiceOnlyInDevelopment(string environment, string expected)
    {
        var services = new ServiceCollection();
        services.AddApiProblemDetails();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environment });
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ProblemDetailsOptions>>().Value;

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature
        {
            Error = UpstreamServiceException.Unreachable("rates", new HttpRequestException("refused"))
        });
        var problem = new ProblemDetails();
        options.CustomizeProblemDetails!(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });

        Assert.Equal(expected, problem.Detail);
    }
}
