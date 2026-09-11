namespace StarterApp.Api.Infrastructure;

public static class WebApplicationExtensions
{
    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        var isDevelopment = app.Environment.IsDevelopment();

        app.Use((context, next) =>
        {
            // UseExceptionHandler clears the response headers before it writes ProblemDetails.
            // An OnStarting callback survives that reset, so the security headers reach the error
            // responses too. The callback assigns rather than appends, so it has the last word.
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
        // This runs after UseRouting, because the authorization metadata is per endpoint, and
        // before UseRateLimiter, because the rate limiter partitions on ICurrentUser.
        // JwtIdentityMiddleware is the only place that copies the validated claims into ICurrentUser.
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
