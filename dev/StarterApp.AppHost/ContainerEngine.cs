using System.ComponentModel;
using System.Diagnostics;

namespace StarterApp.AppHost;

public static class ContainerEngine
{
    // Podman names the field one way and Docker the other; each refuses the other's template.
    private static readonly string[] PlatformTemplates = ["{{.Server.OsArch}}", "{{.Server.Os}}/{{.Server.Arch}}"];

    public static async Task RequestOwnPlatformAsync(ContainerRuntimeArgsCallbackContext context)
    {
        if (await PlatformAsync(context.CancellationToken) is not { } platform)
            return;

        context.Args.Add("--platform");
        context.Args.Add(platform);
    }

    public static async Task<string?> PlatformAsync(CancellationToken cancellationToken)
    {
        var chosen = Environment.GetEnvironmentVariable("ASPIRE_CONTAINER_RUNTIME")
            ?? Environment.GetEnvironmentVariable("DOTNET_ASPIRE_CONTAINER_RUNTIME");

        foreach (var runtime in chosen is null ? ["docker", "podman"] : new[] { chosen })
        {
            foreach (var template in PlatformTemplates)
            {
                if (await VersionAsync(runtime, template, cancellationToken) is { } platform)
                    return platform;
            }
        }

        return null;
    }

    private static async Task<string?> VersionAsync(string runtime, string template, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(runtime, ["version", "--format", template])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(output, error, process.WaitForExitAsync(cancellationToken));
            return process.ExitCode == 0 ? (await output).Trim() : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
