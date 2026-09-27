namespace StarterApp.Api.Infrastructure;

public static class WebApplicationExtensions
{
    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        var isDevelopment = app.Environment.IsDevelopment();

        app.Use((context, next) =>
        {
            // UseExceptionHandler clears response headers; an OnStarting callback survives that, and it assigns rather than appends.
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
        // "0" is the OWASP recommendation: the auditor is gone and enabling it created XS-Leak side channels.
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
        // After UseRouting (authorization metadata is per endpoint) and before UseRateLimiter (it partitions on ICurrentUser).
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
            DbUpdateException dbUpdateException when dbUpdateException.IsStringTruncationViolation => StatusCodes.Status400BadRequest,
            DbUpdateException dbUpdateException when dbUpdateException.IsCheckConstraintViolation() => StatusCodes.Status400BadRequest,
            DbUpdateException dbUpdateException when dbUpdateException.IsNotNullViolation => StatusCodes.Status400BadRequest,
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
            // Bare BCL exceptions are server bugs; they fall through to 500 so alerting sees them.
            _ => StatusCodes.Status500InternalServerError
        };
    }
}
