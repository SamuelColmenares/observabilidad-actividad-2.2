using Microsoft.EntityFrameworkCore;
using Passengers.Data;
using Passengers.Models;
using Xunit;

namespace Passengers.Tests;

/// <summary>
/// Unit tests for PassengerDbContext using Entity Framework Core In-Memory database provider.
/// </summary>
public class PassengerDbContextTests
{
    private PassengerDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<PassengerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new PassengerDbContext(options);
    }

    [Fact]
    public async Task AddPassengerAsync_ShouldPersistPassengerSuccessfully()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var passenger = new Passenger
        {
            Id = "PAS-TEST-001",
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            PassportNumber = "A1234567",
            CreatedAt = DateTime.UtcNow
        };

        // Act
        dbContext.Passengers.Add(passenger);
        await dbContext.SaveChangesAsync();

        // Assert
        var result = await dbContext.Passengers.FindAsync("PAS-TEST-001");
        Assert.NotNull(result);
        Assert.Equal("John", result.FirstName);
        Assert.Equal("Doe", result.LastName);
        Assert.Equal("john.doe@example.com", result.Email);
    }

    [Fact]
    public async Task FindAsync_ShouldReturnNull_WhenPassengerDoesNotExist()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();

        // Act
        var result = await dbContext.Passengers.FindAsync("NON-EXISTENT-ID");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task AddMultiplePassengers_ShouldReturnCorrectCount()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        dbContext.Passengers.AddRange(
            new Passenger { Id = "PAS-001", FirstName = "Alice", LastName = "Smith", Email = "alice@example.com", PassportNumber = "P001" },
            new Passenger { Id = "PAS-002", FirstName = "Bob", LastName = "Jones", Email = "bob@example.com", PassportNumber = "P002" }
        );
        await dbContext.SaveChangesAsync();

        // Act
        var count = await dbContext.Passengers.CountAsync();

        // Assert
        Assert.Equal(2, count);
    }
}
