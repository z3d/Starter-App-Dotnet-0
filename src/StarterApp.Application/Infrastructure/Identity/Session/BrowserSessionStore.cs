using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;

namespace StarterApp.Application.Infrastructure.Identity.Session;

public sealed class BrowserSessionStore
{
    private const string SessionPrefix = "starterapp:browser-session:";
    private const string SignInPrefix = "starterapp:browser-sign-in:";
    public static readonly TimeSpan SignInLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan EndedSessionLifetime = TimeSpan.FromMinutes(10);

    private readonly IDistributedCache _cache;

    public BrowserSessionStore(IDistributedCache cache)
    {
        _cache = cache;
    }

    public static string NewSecret() => Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public Task<BrowserSessionRecord?> GetSessionAsync(string cookieValue, CancellationToken cancellationToken) =>
        GetAsync<BrowserSessionRecord>(SessionPrefix + Hash(cookieValue), cancellationToken);

    public Task SaveSessionAsync(string cookieValue, BrowserSessionRecord session, TimeSpan lifetime, CancellationToken cancellationToken) =>
        SetAsync(SessionPrefix + Hash(cookieValue), session, lifetime, cancellationToken);

    public Task RemoveSessionAsync(string cookieValue, CancellationToken cancellationToken) =>
        _cache.RemoveAsync(SessionPrefix + Hash(cookieValue), cancellationToken);

    public Task<BrowserSignInRecord?> GetSignInAsync(string state, CancellationToken cancellationToken) =>
        GetAsync<BrowserSignInRecord>(SignInPrefix + Hash(state), cancellationToken);

    public Task SaveSignInAsync(string state, BrowserSignInRecord signIn, CancellationToken cancellationToken) =>
        SetAsync(SignInPrefix + Hash(state), signIn, SignInLifetime, cancellationToken);

    public Task RemoveSignInAsync(string state, CancellationToken cancellationToken) =>
        _cache.RemoveAsync(SignInPrefix + Hash(state), cancellationToken);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        var bytes = await _cache.GetAsync(key, cancellationToken);
        return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes);
    }

    private Task SetAsync<T>(string key, T value, TimeSpan lifetime, CancellationToken cancellationToken) =>
        _cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime }, cancellationToken);
}
