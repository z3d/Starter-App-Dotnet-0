using System.Net.Http.Headers;

namespace StarterApp.Tests.Integration;

// One call per route: no token is a 401, a token missing the route's scope (or its MFA) is a 403, and the right token gets past both.
internal static class RouteRefusals
{
    public static async Task AssertAsync(HttpClient client, HttpMethod method, string path, string? requiredScope, bool requiresMfa = false)
    {
        var route = $"{method} {path}";

        using (var anonymous = Request(method, path, token: null))
        using (var response = await client.SendAsync(anonymous))
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{route} without a token answered {(int)response.StatusCode}, not 401.");

        if (requiredScope is not null)
        {
            var otherScopes = string.Join(' ', TestJwtIdentity.DefaultScopes.Split(' ').Where(scope => scope != requiredScope));
            using var wrongScope = Request(method, path, TestJwtIdentity.CreateToken(scopes: otherScopes));
            using var response = await client.SendAsync(wrongScope);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{route} without {requiredScope} answered {(int)response.StatusCode}, not 403.");
            Assert.Contains("insufficient_scope", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        }

        if (requiresMfa)
        {
            using var noMfa = Request(method, path, TestJwtIdentity.CreateToken(authenticationMethods: "pwd"));
            using var response = await client.SendAsync(noMfa);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{route} without MFA answered {(int)response.StatusCode}, not 403.");
        }

        using (var allowed = Request(method, path, TestJwtIdentity.CreateToken()))
        using (var response = await client.SendAsync(allowed))
            Assert.True(response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden),
                $"{route} with the right token answered {(int)response.StatusCode}; it never reached the handler.");
    }

    // A write carries an empty JSON object, so binding succeeds and the endpoint filters run.
    private static HttpRequestMessage Request(HttpMethod method, string path, string? token)
    {
        var request = new HttpRequestMessage(method, path);
        if (method != HttpMethod.Get && method != HttpMethod.Delete)
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
