using Serilog.Enrichers.Sensitive;

namespace StarterApp.Api.Infrastructure;

// The host and SensitiveLogMaskingTests both call Apply, so the tested pipeline is the one that runs.
public static class SerilogConfiguration
{
    public static void Apply(LoggerConfiguration loggerConfiguration, IConfiguration configuration, IServiceProvider? services = null)
    {
        loggerConfiguration.ReadFrom.Configuration(configuration);

        if (services is not null)
            loggerConfiguration.ReadFrom.Services(services);

        loggerConfiguration
            .Enrich.FromLogContext()
            .Enrich.WithSensitiveDataMasking(_ => { });

        var seqUrl = configuration["SEQ_URL"] ?? configuration["SeqUrl"];
        if (!string.IsNullOrEmpty(seqUrl))
            loggerConfiguration.WriteTo.Seq(seqUrl);
    }
}
