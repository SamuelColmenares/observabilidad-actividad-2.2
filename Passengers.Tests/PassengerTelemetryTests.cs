using Passengers.Telemetry;
using Xunit;

namespace Passengers.Tests;

/// <summary>
/// Unit tests for Passengers OpenTelemetry Diagnostics definitions.
/// </summary>
public class PassengerTelemetryTests
{
    [Fact]
    public void Diagnostics_Constants_ShouldMatchExpectedValues()
    {
        Assert.Equal("passengers-service", Diagnostics.ServiceName);
        Assert.Equal("1.0.0", Diagnostics.ServiceVersion);
    }

    [Fact]
    public void RecordMetrics_ShouldNotThrowExceptions()
    {
        // Act & Assert
        var exception = Record.Exception(() =>
        {
            Diagnostics.PassengerCreatedCounter.Add(1);
            Diagnostics.PassengerRetrievedCounter.Add(1);
            Diagnostics.PassengerErrorCounter.Add(1);
            Diagnostics.PassengerDelayHistogram.Record(500);
        });

        Assert.Null(exception);
    }
}
