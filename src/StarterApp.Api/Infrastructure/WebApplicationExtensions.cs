namespace StarterApp.Api.Infrastructure;

public static class WebApplicationExtensions
{
    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        var isDevelopment = app.Environment.IsDevelopment();

        app.Use((context, next) =>
        {
            // Registered as an OnStarting callback rather than written eagerly. UseExceptionHandler
            // calls Response.Clear() before it writes ProblemDetails, which wipes every header set
            // so far; OnStarting callbacks live on the response feature and survive that reset, so
            // error responses carry the same posture as successes. Reordering the middleware does
            // not help — the clear runs regardless of where the writer sits.
            context.Response.OnStarting(static state =>
            {
                var (httpContext, development) = ((HttpContext, bool))state;
                ApplySecurityHeaders(httpContext, development);
                return Task.CompletedTask;
            }, (context, isDevelopment));

            return next();
        });

        return app;
    }

    internal static void ApplySecurityHeaders(HttpContext context, bool isDevelopment)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        // "0" is the current OWASP recommendation: modern browsers no longer ship the XSS
        // auditor, and enabling it ("1; mode=block") created XS-Leak side channels in the
        // browsers that did.
        headers["X-XSS-Protection"] = "0";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        if (isDevelopment)
            return;

        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'";

        // Replaces app.UseHsts(), whose middleware writes the header eagerly and loses it on the
        // same reset. Same policy as HstsMiddleware's defaults: 30-day max-age, https requests
        // only, loopback hosts excluded so a local https run never pins the browser.
        if (context.Request.IsHttps && !IsLoopbackHost(context.Request.Host.Host))
            headers["Strict-Transport-Security"] = "max-age=2592000";
    }

    private static bool IsLoopbackHost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host is "127.0.0.1" or "[::1]" or "::1";
    }

    public static WebApplication UseExceptionHandling(this WebApplication app)
    {
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            StatusCodeSelector = ResolveExceptionStatusCode
        });

        app.UseStatusCodePages();

        return app;
    }

    public static WebApplication UseJwtIdentity(this WebApplication app)
    {
        // Authentication validates the bearer token, authorization enforces the endpoints'
        // RequireAuthorization metadata, and JwtIdentityMiddleware is the single writer that
        // projects validated claims onto the scoped ICurrentUser. Runs after UseRouting (the
        // authorize metadata is per-endpoint) and before UseRateLimiter (partitioning reads
        // ICurrentUser).
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<JwtIdentityMiddleware>();
        return app;
    }

    internal static int ResolveExceptionStatusCode(Exception ex)
    {
        return ex switch
        {
            OperationCanceledException => 499,
            DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
            DbUpdateException dbUpdateException when dbUpdateException.IsUniqueConstraintViolation() => StatusCodes.Status409Conflict,
            DbUpdateException dbUpdateException when dbUpdateException.IsForeignKeyViolation() => StatusCodes.Status409Conflict,
            DbUpdateException dbUpdateException when dbUpdateException.IsStringTruncationViolation() => StatusCodes.Status400BadRequest,
            DbUpdateException dbUpdateException when dbUpdateException.IsCheckConstraintViolation() => StatusCodes.Status400BadRequest,
            DbUpdateException dbUpdateException when dbUpdateException.IsNotNullViolation() => StatusCodes.Status400BadRequest,
            ValidationException => StatusCodes.Status400BadRequest,
            ForbiddenAccessException => StatusCodes.Status403Forbidden,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            ArgumentNullException => StatusCodes.Status400BadRequest,
            ArgumentOutOfRangeException => StatusCodes.Status400BadRequest,
            ArgumentException => StatusCodes.Status400BadRequest,
            BadHttpRequestException => StatusCodes.Status400BadRequest,
            EntityNotFoundException => StatusCodes.Status404NotFound,
            FeatureToggles.FeatureDisabledException => StatusCodes.Status503ServiceUnavailable,
            DomainRuleException => StatusCodes.Status409Conflict,
            // Bare BCL InvalidOperationException/KeyNotFoundException are server bugs, not
            // client faults — they fall through to 500 so alerting sees them.
            _ => StatusCodes.Status500InternalServerError
        };
    }
}
