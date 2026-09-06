using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Infrastructure.Payloads;

public class PayloadRedactionRegressionTests
{
    [Theory]
    [InlineData("application/json")]
    [InlineData(null)]
    public async Task CaptureAsync_WithMalformedJson_SuppressesSensitiveLogValues(string? contentType)
    {
        const string payload = "{\"password\":\"sentinel-password\",";
        var store = new InMemoryPayloadArchiveStore();
        var logger = new RecordingLogger();
        var sink = CreateSink(store, logger, new PayloadCaptureOptions());

        var record = await sink.CaptureAsync(new PayloadCaptureRequest
        {
            Channel = "http",
            Operation = "POST /api/v1/customers",
            ContentType = contentType,
            Payload = payload
        }, CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal(payload, record.Payload);
        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("sentinel-password", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvokeAsync_WhenJsonCaptureIsTruncated_SuppressesSensitiveLogValues()
    {
        const string prefix = "{\"password\":\"sentinel-password\",";
        var options = new PayloadCaptureOptions { MaxPayloadBytes = Encoding.UTF8.GetByteCount(prefix) };
        var store = new InMemoryPayloadArchiveStore();
        var logger = new RecordingLogger();
        var sink = CreateSink(store, logger, options);
        var middleware = new PayloadCaptureMiddleware(_ => Task.CompletedTask, sink,
            Options.Create(options), NullLogger<PayloadCaptureMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/customers";
        context.Request.ContentType = "application/json";
        context.Request.Headers[CorrelationContext.HeaderName] = "truncated-json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(prefix + "\"padding\":\"more text\"}"));
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        var archive = store.Lines.Single(pair => pair.Key.EndsWith("/truncated-json.jsonl", StringComparison.Ordinal));
        using var row = JsonDocument.Parse(archive.Value[0]);
        Assert.True(row.RootElement.GetProperty("payloadTruncated").GetBoolean());
        Assert.Equal(prefix, row.RootElement.GetProperty("payload").GetString());
        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("sentinel-password", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"password\":{\"customerId\":\"sentinel-private-id\"},\"customerId\":42}", "password")]
    [InlineData("{\"privateDetails\":[{\"nested\":{\"customerId\":\"sentinel-private-id\"}}],\"customerId\":42}", "private")]
    public async Task CaptureAsync_WithSensitiveAncestor_ExcludesTheWholeSubtreeFromIndexesAndLogs(string payload, string sensitiveName)
    {
        var store = new InMemoryPayloadArchiveStore();
        var logger = new RecordingLogger();
        var sink = CreateSink(store, logger, new PayloadCaptureOptions { SensitivePropertyNames = [sensitiveName] });

        var record = await sink.CaptureAsync(new PayloadCaptureRequest
        {
            Channel = "http",
            Operation = "POST /api/v1/customers",
            ContentType = "application/json",
            Payload = payload
        }, CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal(payload, record.Payload);
        var reference = Assert.Single(record.EntityReferences);
        Assert.Equal("42", reference.EntityId);
        Assert.DoesNotContain(store.Lines.Keys, key => key.Contains("sentinel-private-id", StringComparison.Ordinal));
        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("sentinel-private-id", StringComparison.Ordinal));
        Assert.Contains(store.Lines.Keys, key => key.StartsWith("entity-index/customer/42/", StringComparison.Ordinal));
    }

    private static PayloadCaptureSink CreateSink(InMemoryPayloadArchiveStore store, RecordingLogger logger, PayloadCaptureOptions options)
        => new(store, new JsonPayloadRedactor(Options.Create(options)), TimeProvider.System, Options.Create(options), logger);

    private sealed class RecordingLogger : ILogger<PayloadCaptureSink>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
