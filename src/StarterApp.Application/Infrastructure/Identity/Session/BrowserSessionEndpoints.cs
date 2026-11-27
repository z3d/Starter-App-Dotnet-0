using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace StarterApp.Application.Infrastructure.Identity.Session;

public static class BrowserSessionEndpoints
{
    public const string LoginPath = "/api/auth/login";
    public const string CallbackPath = "/api/auth/callback";
    public const string SessionPath = "/api/auth/session";
    public const string LogoutPath = "/api/auth/logout";
    public const string SignedOutPage = "/signed-out";
    public const string CsrfHeader = "X-CSRF-Token";
    public const string EndedHeader = "X-Session-Ended";

    private static readonly HashSet<string> SignOutReasons = new(StringComparer.Ordinal) { "signed-out", "idle", "expired" };

    public static IEndpointRouteBuilder MapBrowserSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(LoginPath, SignIn).AllowAnonymous().ExcludeFromDescription();
        app.MapGet(CallbackPath, Callback).AllowAnonymous().ExcludeFromDescription();
        app.MapPost(LogoutPath, SignOut).AllowAnonymous().ExcludeFromDescription();
        app.MapGet(SessionPath, Describe)
            .WithTags("Session")
            .WithName("GetBrowserSession")
            .WithSummary("Who is signed in through this browser session, and the token a write must echo")
            .RequireAuthorization()
            .Produces<BrowserSessionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        return app;
    }

    public static bool IsSessionRoute(PathString path) =>
        path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase)
        || path.Equals(CallbackPath, StringComparison.OrdinalIgnoreCase)
        || path.Equals(LogoutPath, StringComparison.OrdinalIgnoreCase);

    public static bool SameSecret(string? presented, string expected) =>
        presented is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected));

    private static async Task<IResult> SignIn(
        HttpContext context,
        [FromQuery] string? returnTo,
        [FromQuery] string? scope,
        [FromQuery(Name = "acr_values")] string? acrValues,
        [FromServices] BrowserSessionStore store,
        [FromServices] IdentityProviderClient provider,
        IOptions<BrowserSessionOptions> sessionOptions,
        IOptions<JwtIdentityOptions> identityOptions,
        CancellationToken cancellationToken)
    {
        var options = sessionOptions.Value;
        if (!options.Enabled)
            return NotConfigured();

        var requested = scope is null ? options.Scopes : scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requested.Any(item => !options.Scopes.Contains(item, StringComparer.Ordinal)) || !requested.Contains("openid", StringComparer.Ordinal))
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: "A sign-in may ask only for scopes the session is configured with, and always for openid.");

        Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration configuration;
        try
        {
            configuration = await provider.ConfigurationAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return ProviderUnavailable();
        }

        var state = BrowserSessionStore.NewSecret();
        var verifier = BrowserSessionStore.NewSecret();
        var binding = BrowserSessionStore.NewSecret();
        await store.SaveSignInAsync(state, new BrowserSignInRecord
        {
            CodeVerifier = verifier,
            BrowserBinding = binding,
            ReturnTo = LocalPath(returnTo),
        }, cancellationToken);
        context.Response.Cookies.Append(BrowserSessionCookies.SignIn, binding, BrowserSessionCookies.ForSignIn());

        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = RedirectUri(options).ToString(),
            ["scope"] = string.Join(' ', requested),
            ["state"] = state,
            ["code_challenge"] = Base64UrlTextEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256",
        };
        if (!string.IsNullOrWhiteSpace(options.Audience))
            query["audience"] = options.Audience;
        if (!string.IsNullOrWhiteSpace(options.Organization))
            query["organization"] = options.Organization;
        if (!string.IsNullOrWhiteSpace(acrValues) && string.Equals(acrValues, identityOptions.Value.StepUpAcrValues, StringComparison.Ordinal))
            query["acr_values"] = acrValues;

        return Results.Redirect(QueryHelpers.AddQueryString(configuration.AuthorizationEndpoint, query));
    }

    private static async Task<IResult> Callback(
        HttpContext context,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromServices] BrowserSessionStore store,
        [FromServices] IdentityProviderClient provider,
        IOptions<BrowserSessionOptions> sessionOptions,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var options = sessionOptions.Value;
        if (!options.Enabled)
            return NotConfigured();

        var logger = loggerFactory.CreateLogger(typeof(BrowserSessionEndpoints));
        var binding = context.Request.Cookies[BrowserSessionCookies.SignIn];
        context.Response.Cookies.Delete(BrowserSessionCookies.SignIn, BrowserSessionCookies.ForSignIn());

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            logger.LogWarning("Sign-in came back without a code: {SignInError}", error ?? "no code or state");
            return Results.Redirect(SignedOut("failed"));
        }

        var signIn = await store.GetSignInAsync(state, cancellationToken);
        if (signIn is null || !SameSecret(binding, signIn.BrowserBinding))
        {
            logger.LogWarning("Sign-in callback refused: its state is unknown, used or from another browser");
            return Results.Redirect(SignedOut("failed"));
        }

        await store.RemoveSignInAsync(state, cancellationToken);

        IdentityProviderTokens tokens;
        try
        {
            tokens = await provider.ExchangeCodeAsync(code, signIn.CodeVerifier, RedirectUri(options), cancellationToken);
        }
        catch (Exception ex) when (ex is IdentityProviderRefusedException or HttpRequestException or InvalidOperationException)
        {
            logger.LogWarning(ex, "The identity provider would not exchange the sign-in code");
            return Results.Redirect(SignedOut("failed"));
        }

        if (context.Request.Cookies[BrowserSessionCookies.Session] is { Length: > 0 } previous)
            await store.RemoveSessionAsync(previous, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var sessionId = BrowserSessionStore.NewSecret();
        await store.SaveSessionAsync(sessionId, new BrowserSessionRecord
        {
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            IdToken = tokens.IdToken,
            AccessTokenExpiresAt = now.AddSeconds(tokens.ExpiresInSeconds),
            CreatedAt = now,
            LastSeenAt = now,
            CsrfToken = BrowserSessionStore.NewSecret(),
        }, TimeSpan.FromHours(options.MaxHours), cancellationToken);
        context.Response.Cookies.Append(BrowserSessionCookies.Session, sessionId, BrowserSessionCookies.ForSession());

        return Results.Redirect(signIn.ReturnTo);
    }

    private static async Task<IResult> SignOut(
        HttpContext context,
        [FromServices] BrowserSessionStore store,
        [FromServices] IdentityProviderClient provider,
        IOptions<BrowserSessionOptions> sessionOptions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var options = sessionOptions.Value;
        if (!options.Enabled)
            return NotConfigured();

        var form = context.Request.HasFormContentType ? await context.Request.ReadFormAsync(cancellationToken) : null;
        var reason = form?["reason"].ToString() is { } given && SignOutReasons.Contains(given) ? given : "signed-out";
        var cookie = context.Request.Cookies[BrowserSessionCookies.Session];
        var session = string.IsNullOrEmpty(cookie) ? null : await store.GetSessionAsync(cookie, cancellationToken);

        if (session is not null && session.EndedBecause is null
            && !SameSecret(form?["csrf"].ToString() ?? context.Request.Headers[CsrfHeader].ToString(), session.CsrfToken))
            return CsrfRefused();

        context.Response.Cookies.Delete(BrowserSessionCookies.Session, BrowserSessionCookies.ForSession());
        if (session is null)
            return Results.Redirect(SignedOut(reason));

        await store.RemoveSessionAsync(cookie!, cancellationToken);
        if (session.RefreshToken is { } refreshToken)
        {
            try
            {
                await provider.RevokeAsync(refreshToken, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                loggerFactory.CreateLogger(typeof(BrowserSessionEndpoints)).LogWarning(ex, "The refresh token could not be revoked at sign-out");
            }
        }

        try
        {
            var configuration = await provider.ConfigurationAsync(cancellationToken);
            if (!string.IsNullOrEmpty(configuration.EndSessionEndpoint))
            {
                var query = new Dictionary<string, string?>
                {
                    ["client_id"] = options.ClientId,
                    ["post_logout_redirect_uri"] = options.WebOrigin!.TrimEnd('/') + SignedOut(reason),
                };
                if (!string.IsNullOrEmpty(session.IdToken))
                    query["id_token_hint"] = session.IdToken;
                return Results.Redirect(QueryHelpers.AddQueryString(configuration.EndSessionEndpoint, query));
            }
        }
        catch (InvalidOperationException)
        {
        }

        return Results.Redirect(SignedOut(reason));
    }

    private static IResult Describe(HttpContext context, IOptions<BrowserSessionOptions> sessionOptions)
    {
        var principal = context.User;
        var expires = long.TryParse(principal.FindFirstValue("exp"), CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : (DateTimeOffset?)null;
        var scopes = principal.FindAll("scope").Concat(principal.FindAll("scp")).Select(claim => claim.Value);
        return Results.Ok(new BrowserSessionResponse
        {
            Subject = principal.FindFirstValue("sub") ?? string.Empty,
            TenantId = principal.FindFirstValue("tid") ?? string.Empty,
            DisplayName = principal.FindFirstValue("name") ?? principal.FindFirstValue("preferred_username"),
            Scope = string.Join(' ', scopes),
            AuthenticationMethods = principal.FindAll("amr").Select(claim => claim.Value).ToList(),
            AccessExpiresAt = expires,
            CsrfToken = BrowserSessionMiddleware.CurrentSession(context)?.CsrfToken,
            IdleMinutes = sessionOptions.Value.IdleMinutes,
        });
    }

    internal static IResult CsrfRefused() =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
            detail: $"A change made with this browser session must carry its {CsrfHeader} header.");

    private static IResult NotConfigured() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found",
            detail: "This deployment signs nobody in through a browser session: Identity:Session:ClientId is not set.");

    private static IResult ProviderUnavailable() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service Unavailable",
            detail: "The identity provider could not be reached, so sign-in cannot start. Try again shortly.");

    private static Uri RedirectUri(BrowserSessionOptions options) => new(options.WebOrigin!.TrimEnd('/') + CallbackPath);

    private static string SignedOut(string reason) => $"{SignedOutPage}?reason={reason}";

    // Only a path on this site: "//host" and "/\host" are other sites to a browser.
    internal static string LocalPath(string? returnTo) =>
        returnTo is { Length: > 0 } path && path[0] == '/' && (path.Length == 1 || (path[1] != '/' && path[1] != '\\'))
            ? path
            : "/";
}
