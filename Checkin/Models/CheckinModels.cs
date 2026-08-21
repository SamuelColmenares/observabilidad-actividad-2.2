namespace Checkin.Models;

/// <summary>
/// DTO representing the incoming check-in request payload.
/// </summary>
public record CheckinRequest(
    string PassengerId,
    string FlightNumber,
    string SeatNumber,
    int BaggageCount
);

/// <summary>
/// Check-in record document stored in Couchbase.
/// </summary>
public class CheckinRecord
{
    public string Id { get; set; } = string.Empty;
    public string PassengerId { get; set; } = string.Empty;
    public string FlightNumber { get; set; } = string.Empty;
    public string SeatNumber { get; set; } = string.Empty;
    public int BaggageCount { get; set; }
    public string Status { get; set; } = "COMPLETED";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Passenger model returned from Passengers service HTTP call.
/// </summary>
public record PassengerDto(
    string Id,
    string FirstName,
    string LastName,
    string Email,
    string PassportNumber,
    DateTime CreatedAt
);
