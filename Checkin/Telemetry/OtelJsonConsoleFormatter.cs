using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Checkin.Telemetry;

/// <summary>
/// Console log formatter that emits structured JSON lines enriched with trace_id/span_id
/// taken from the current OpenTelemetry Activity, enabling correlation between logs and
/// traces (e.g. Grafana Explore, Cloud Logging → Jaeger via Correlations).
/// </summary>
public sealed class OtelJsonConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "otel-json";

    public OtelJsonConsoleFormatter() : base(FormatterName)
    {
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
        {
            return;
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("timestamp", DateTime.UtcNow.ToString("O"));
            writer.WriteString("level", logEntry.LogLevel.ToString());
            writer.WriteString("category", logEntry.Category);
            writer.WriteString("service", Diagnostics.ServiceName);
            writer.WriteString("message", message);

            // Pivote de correlación: trace_id/span_id del Activity activo (W3C, 32/16 dígitos hex).
            var activity = Activity.Current;
            if (activity is not null)
            {
                writer.WriteString("trace_id", activity.TraceId.ToHexString());
                writer.WriteString("span_id", activity.SpanId.ToHexString());
            }

            if (logEntry.Exception is not null)
            {
                writer.WriteString("exception", logEntry.Exception.ToString());
            }

            writer.WriteEndObject();
        }

        textWriter.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }
}
