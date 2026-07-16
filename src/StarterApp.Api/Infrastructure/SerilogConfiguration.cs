using Serilog.Enrichers.Sensitive;

namespace StarterApp.Api.Infrastructure;

// Single source of truth for the API's Serilog pipeline. The host and the masking regression
// test (SensitiveLogMaskingTests) both call Apply, so the composition that is tested is always
// the composition that runs — sensitive-data masking cannot silently drop out of either path.
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
