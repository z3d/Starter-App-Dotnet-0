namespace StarterApp.Application.Infrastructure.Upstream;

public static class UpstreamResponseExtensions
{
    public static HttpResponseMessage EnsureUpstreamSuccess(this HttpResponseMessage response, string service)
    {
        if (response.IsSuccessStatusCode)
            return response;

        var status = (int)response.StatusCode;
        var transient = status >= 500 || status is StatusCodes.Status408RequestTimeout or StatusCodes.Status429TooManyRequests;
        throw new UpstreamServiceException(service, isPermanent: !transient,
            $"The outside service '{service}' answered {status} after its retries.");
    }
}
