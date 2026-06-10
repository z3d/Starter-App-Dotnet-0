namespace StarterApp.Api.Infrastructure;

public static class WebApplicationExtensions
{
    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Append("X-Frame-Options", "DENY");
            // "0" is the current OWASP recommendation: modern browsers no longer ship the XSS
            // auditor, and enabling it ("1; mode=block") created XS-Leak side channels in the
            // browsers that did.
            context.Response.Headers.Append("X-XSS-Protection", "0");
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

            if (!app.Environment.IsDevelopment())
                context.Response.Headers.Append("Content-Security-Policy",
                    "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'");

            await next();
        });

        return app;
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

    public static WebApplication UseGatewayIdentity(this WebApplication app)
    {
        app.UseMiddleware<GatewayIdentityMiddleware>();
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
            KeyNotFoundException => StatusCodes.Status404NotFound,
            InvalidOperationException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
    }
}
