using Checkin.Telemetry;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Unit tests for Checkin OpenTelemetry Diagnostics definitions.
/// </summary>
public class CheckinTelemetryTests
{
    [Fact]
    public void Diagnostics_Constants_ShouldMatchExpectedValues()
    {
        Assert.Equal("checkin-service", Diagnostics.ServiceName);
        Assert.Equal("1.0.0", Diagnostics.ServiceVersion);
    }

    [Fact]
    public void RecordMetrics_ShouldNotThrowExceptions()
    {
        // Act & Assert
        var exception = Record.Exception(() =>
        {
            Diagnostics.CheckinRequestCounter.Add(1);
            Diagnostics.CheckinSuccessCounter.Add(1);
            Diagnostics.CheckinErrorCounter.Add(1);
            Diagnostics.CheckinDelayHistogram.Record(250);
        });

        Assert.Null(exception);
    }
}
