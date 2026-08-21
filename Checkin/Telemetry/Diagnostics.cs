using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Checkin.Telemetry;

/// <summary>
/// Holds OpenTelemetry ActivitySource and Meter metrics definitions for Checkin microservice.
/// </summary>
public static class Diagnostics
{
    public const string ServiceName = "checkin-service";
    public const string ServiceVersion = "1.0.0";

    /// <summary>
    /// ActivitySource for manual span instrumentation.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);

    /// <summary>
    /// Meter for custom metrics.
    /// </summary>
    public static readonly Meter Meter = new(ServiceName, ServiceVersion);

    // Custom Metric Instruments
    public static readonly Counter<long> CheckinRequestCounter = Meter.CreateCounter<long>(
        "checkin.requests.count",
        description: "Total number of check-in requests processed");

    public static readonly Counter<long> CheckinSuccessCounter = Meter.CreateCounter<long>(
        "checkin.success.count",
        description: "Number of successful passenger check-ins");

    public static readonly Counter<long> CheckinErrorCounter = Meter.CreateCounter<long>(
        "checkin.errors.count",
        description: "Number of check-in errors or failed validations");

    public static readonly Histogram<double> CheckinDelayHistogram = Meter.CreateHistogram<double>(
        "checkin.delay.ms",
        unit: "ms",
        description: "Artificial response delays simulated in milliseconds");
}
