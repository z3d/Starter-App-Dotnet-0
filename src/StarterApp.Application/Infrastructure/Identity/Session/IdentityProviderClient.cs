using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace StarterApp.Application.Infrastructure.Identity.Session;

public sealed class IdentityProviderClient
{
    public const string HttpClientName = "identity-provider";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<JwtBearerOptions> _bearerOptions;
    private readonly IOptions<JwtIdentityOptions> _identityOptions;
    private readonly IOptions<BrowserSessionOptions> _sessionOptions;

    public IdentityProviderClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<JwtBearerOptions> bearerOptions,
        IOptions<JwtIdentityOptions> identityOptions,
        IOptions<BrowserSessionOptions> sessionOptions)
    {
        _httpClientFactory = httpClientFactory;
        _bearerOptions = bearerOptions;
        _identityOptions = identityOptions;
        _sessionOptions = sessionOptions;
    }

    public async Task<OpenIdConnectConfiguration> ConfigurationAsync(CancellationToken cancellationToken)
    {
        var manager = _bearerOptions.Get(JwtBearerDefaults.AuthenticationScheme).ConfigurationManager
            ?? throw new InvalidOperationException("The bearer handler has no configuration manager, so the identity provider's endpoints are unknown.");
        return await manager.GetConfigurationAsync(cancellationToken);
    }

    public Task<IdentityProviderTokens> ExchangeCodeAsync(string code, string codeVerifier, Uri redirectUri, CancellationToken cancellationToken) =>
        RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri.ToString(),
        }, cancellationToken);

    public Task<IdentityProviderTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, cancellationToken);

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var configuration = await ConfigurationAsync(cancellationToken);
        if (configuration.AdditionalData.TryGetValue("revocation_endpoint", out var endpoint) && endpoint is string address)
        {
            using var response = await SendAsync(address, new Dictionary<string, string>
            {
                ["token"] = refreshToken,
                ["token_type_hint"] = "refresh_token",
            }, cancellationToken);
        }
    }

    private async Task<IdentityProviderTokens> RequestTokensAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var configuration = await ConfigurationAsync(cancellationToken);
        using var response = await SendAsync(configuration.TokenEndpoint, form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var refused = (int)response.StatusCode is >= 400 and < 500;
            throw new IdentityProviderRefusedException(refused, $"The identity provider's token endpoint answered {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        return new IdentityProviderTokens(
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
            root.TryGetProperty("id_token", out var id) ? id.GetString() : null,
            root.TryGetProperty("expires_in", out var expiresIn) ? expiresIn.GetInt32() : 300);
    }

    private async Task<HttpResponseMessage> SendAsync(string address, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var session = _sessionOptions.Value;
        form["client_id"] = session.ClientId!;
        using var request = new HttpRequestMessage(HttpMethod.Post, BackChannel(address)) { Content = new FormUrlEncodedContent(form) };
        if (!string.IsNullOrEmpty(session.ClientSecret))
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(session.ClientId!)}:{Uri.EscapeDataString(session.ClientSecret)}")));
        return await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
    }

    // Discovery fetched over an internal hostname still names the public one, which the API may not be able to reach.
    private string BackChannel(string address)
    {
        var metadata = _identityOptions.Value.MetadataAddress;
        if (string.IsNullOrWhiteSpace(metadata))
            return address;
        var internalOrigin = new Uri(metadata);
        return new UriBuilder(address) { Scheme = internalOrigin.Scheme, Host = internalOrigin.Host, Port = internalOrigin.Port }.Uri.ToString();
    }
}
