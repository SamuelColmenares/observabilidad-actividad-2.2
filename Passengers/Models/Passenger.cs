namespace Passengers.Models;

/// <summary>
/// Represents a passenger entity stored in PostgreSQL.
/// </summary>
public class Passenger
{
    /// <summary>
    /// Unique passenger identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Passenger's first name.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Passenger's last name.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Passenger's email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Passenger's passport number.
    /// </summary>
    public string PassportNumber { get; set; } = string.Empty;

    /// <summary>
    /// Creation timestamp in UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Data transfer object for passenger creation.
/// </summary>
public record CreatePassengerDto(
    string? Id,
    string FirstName,
    string LastName,
    string Email,
    string PassportNumber
);
