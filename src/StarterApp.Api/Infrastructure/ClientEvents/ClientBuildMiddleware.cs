using Microsoft.Extensions.Options;

namespace StarterApp.Api.Infrastructure.ClientEvents;

// Every call from the web app names its build. A build other than the deployed one is told to reload, and an operator's notice rides on every response.
public sealed class ClientBuildMiddleware
{
    public const string BuildHeader = "X-Client-Build";
    public const string ReloadHeader = "X-Client-Reload";
    public const string NoticeHeader = "X-Service-Notice";

    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<ClientOptions> _options;

    public ClientBuildMiddleware(RequestDelegate next, IOptionsMonitor<ClientOptions> options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var build = Sanitize(context.Request.Headers[BuildHeader].ToString());
        var options = _options.CurrentValue;

        context.Response.OnStarting(() =>
        {
            if (IsStale(build, options.ExpectedBuild))
                context.Response.Headers[ReloadHeader] = "true";
            if (!string.IsNullOrWhiteSpace(options.ServiceNotice))
                context.Response.Headers[NoticeHeader] = Uri.EscapeDataString(options.ServiceNotice.Trim());
            return Task.CompletedTask;
        });

        using (Serilog.Context.LogContext.PushProperty("ClientBuild", build ?? "(none)"))
            await _next(context);
    }

    // A local build ("dev") and a caller that names no build are never told to reload.
    internal static bool IsStale(string? build, string? expectedBuild) =>
        build is not null && build != "dev" && !string.IsNullOrWhiteSpace(expectedBuild) &&
        !string.Equals(build, expectedBuild.Trim(), StringComparison.OrdinalIgnoreCase);

    internal static string? Sanitize(string value) =>
        value.Length is > 0 and <= 64 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
            ? value
            : null;
}
