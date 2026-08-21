using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Passengers.Telemetry;

/// <summary>
/// Holds OpenTelemetry ActivitySource and Meter metrics definitions for Passengers microservice.
/// </summary>
public static class Diagnostics
{
    public const string ServiceName = "passengers-service";
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
    public static readonly Counter<long> PassengerCreatedCounter = Meter.CreateCounter<long>(
        "passengers.created.count",
        description: "Number of passengers successfully created");

    public static readonly Counter<long> PassengerRetrievedCounter = Meter.CreateCounter<long>(
        "passengers.retrieved.count",
        description: "Number of passenger retrieval requests");

    public static readonly Counter<long> PassengerErrorCounter = Meter.CreateCounter<long>(
        "passengers.errors.count",
        description: "Number of passenger service errors");

    public static readonly Histogram<double> PassengerDelayHistogram = Meter.CreateHistogram<double>(
        "passengers.request.delay.ms",
        unit: "ms",
        description: "Artificial response delays simulated in milliseconds");
}
