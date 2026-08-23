using Checkin.Models;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Unit tests for Checkin models and DTO records.
/// </summary>
public class CheckinModelTests
{
    [Fact]
    public void CheckinRequest_ShouldInitializePropertiesCorrectly()
    {
        // Act
        var request = new CheckinRequest("PAS-100", "AV204", "12A", 2);

        // Assert
        Assert.Equal("PAS-100", request.PassengerId);
        Assert.Equal("AV204", request.FlightNumber);
        Assert.Equal("12A", request.SeatNumber);
        Assert.Equal(2, request.BaggageCount);
    }

    [Fact]
    public void CheckinRecord_ShouldSetDefaultStatusAndTimestamp()
    {
        // Act
        var record = new CheckinRecord
        {
            Id = "CHK-001",
            PassengerId = "PAS-100",
            FlightNumber = "AV204",
            SeatNumber = "12A",
            BaggageCount = 1
        };

        // Assert
        Assert.Equal("CHK-001", record.Id);
        Assert.Equal("COMPLETED", record.Status);
        Assert.True(record.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void PassengerDto_ShouldMapPropertiesCorrectly()
    {
        // Act
        var dto = new PassengerDto("PAS-100", "Jane", "Doe", "jane@example.com", "P123456", DateTime.UtcNow);

        // Assert
        Assert.Equal("PAS-100", dto.Id);
        Assert.Equal("Jane", dto.FirstName);
        Assert.Equal("Doe", dto.LastName);
        Assert.Equal("jane@example.com", dto.Email);
    }
}
