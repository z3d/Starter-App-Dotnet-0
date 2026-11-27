namespace StarterApp.Application.Infrastructure.Identity.Session;

public static class BrowserSessionCookies
{
    public const string Session = "__Host-starterapp-session";
    public const string SignIn = "__Host-starterapp-sign-in";

    // Strict: the session is only ever sent by the app's own pages. The sign-in cookie must survive the provider's redirect back, so it is Lax.
    public static CookieOptions ForSession() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true,
    };

    public static CookieOptions ForSignIn() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true,
        MaxAge = BrowserSessionStore.SignInLifetime,
    };
}
