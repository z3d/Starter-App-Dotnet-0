using Microsoft.Extensions.Options;

namespace StarterApp.Application.Infrastructure.Identity.Session;

internal sealed class BrowserSessionMiddleware
{
    private static readonly object SessionKey = new();
    private static readonly TimeSpan LastSeenResolution = TimeSpan.FromSeconds(15);

    private readonly RequestDelegate _next;

    public BrowserSessionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public static BrowserSessionRecord? CurrentSession(HttpContext context) =>
        context.Items.TryGetValue(SessionKey, out var session) ? session as BrowserSessionRecord : null;

    public async Task InvokeAsync(
        HttpContext context,
        BrowserSessionStore store,
        IdentityProviderClient provider,
        IOptions<BrowserSessionOptions> sessionOptions,
        TimeProvider timeProvider,
        ILogger<BrowserSessionMiddleware> logger)
    {
        var options = sessionOptions.Value;
        var cookie = context.Request.Cookies[BrowserSessionCookies.Session];
        if (!options.Enabled
            || string.IsNullOrEmpty(cookie)
            || context.Request.Headers.ContainsKey("Authorization")
            || BrowserSessionEndpoints.IsSessionRoute(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var cancellationToken = context.RequestAborted;
        var session = await store.GetSessionAsync(cookie, cancellationToken);
        if (session is null)
        {
            context.Response.Cookies.Delete(BrowserSessionCookies.Session, BrowserSessionCookies.ForSession());
            await _next(context);
            return;
        }

        if (session.EndedBecause is { } ended)
        {
            context.Response.Headers[BrowserSessionEndpoints.EndedHeader] = ended;
            await _next(context);
            return;
        }

        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method)
            && !BrowserSessionEndpoints.SameSecret(context.Request.Headers[BrowserSessionEndpoints.CsrfHeader].ToString(), session.CsrfToken))
        {
            await BrowserSessionEndpoints.CsrfRefused().ExecuteAsync(context);
            return;
        }

        var now = timeProvider.GetUtcNow();
        var endReason = now - session.LastSeenAt > TimeSpan.FromMinutes(options.IdleMinutes) ? "idle"
            : now - session.CreatedAt > TimeSpan.FromHours(options.MaxHours) ? "expired"
            : null;

        var expiryBefore = session.AccessTokenExpiresAt;
        if (endReason is null && session.AccessTokenExpiresAt - now <= TimeSpan.FromSeconds(options.RefreshBeforeExpirySeconds))
            endReason = await RefreshAsync(session, provider, now, logger, cancellationToken);

        if (endReason is not null)
        {
            await EndAsync(cookie, session, endReason, store, provider, logger, cancellationToken);
            context.Response.Headers[BrowserSessionEndpoints.EndedHeader] = endReason;
            await _next(context);
            return;
        }

        if (now - session.LastSeenAt >= LastSeenResolution || session.AccessTokenExpiresAt != expiryBefore)
        {
            session.LastSeenAt = now;
            await store.SaveSessionAsync(cookie, session, session.CreatedAt.AddHours(options.MaxHours) - now, cancellationToken);
        }

        context.Items[SessionKey] = session;
        context.Request.Headers.Authorization = $"Bearer {session.AccessToken}";
        await _next(context);
    }

    private static async Task<string?> RefreshAsync(
        BrowserSessionRecord session, IdentityProviderClient provider, DateTimeOffset now, Microsoft.Extensions.Logging.ILogger logger, CancellationToken cancellationToken)
    {
        if (session.RefreshToken is null)
            return session.AccessTokenExpiresAt <= now ? "expired" : null;

        try
        {
            var tokens = await provider.RefreshAsync(session.RefreshToken, cancellationToken);
            session.AccessToken = tokens.AccessToken;
            session.RefreshToken = tokens.RefreshToken ?? session.RefreshToken;
            session.IdToken = tokens.IdToken ?? session.IdToken;
            session.AccessTokenExpiresAt = now.AddSeconds(tokens.ExpiresInSeconds);
            return null;
        }
        catch (IdentityProviderRefusedException ex) when (ex.Refused)
        {
            logger.LogInformation("The identity provider refused to refresh a browser session; it has ended there");
            return "expired";
        }
        catch (Exception ex) when (ex is IdentityProviderRefusedException or HttpRequestException or InvalidOperationException)
        {
            logger.LogWarning(ex, "A browser session's token could not be refreshed; the current one is used while it lasts");
            return null;
        }
    }

    private static async Task EndAsync(
        string cookie, BrowserSessionRecord session, string reason, BrowserSessionStore store, IdentityProviderClient provider, Microsoft.Extensions.Logging.ILogger logger,
        CancellationToken cancellationToken)
    {
        var refreshToken = session.RefreshToken;
        session.EndedBecause = reason;
        session.AccessToken = null;
        session.RefreshToken = null;
        await store.SaveSessionAsync(cookie, session, BrowserSessionStore.EndedSessionLifetime, cancellationToken);
        if (refreshToken is null)
            return;

        try
        {
            await provider.RevokeAsync(refreshToken, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            logger.LogWarning(ex, "An ended browser session's refresh token could not be revoked");
        }
    }
}
