using System.Net;
using System.Net.Http.Json;
using Checkin.Models;
using Moq;
using Moq.Protected;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// Unit tests for HTTP passenger validation calls from Checkin to Passengers microservice.
/// </summary>
public class PassengerValidationHandlerTests
{
    [Fact]
    public async Task ValidatePassenger_ShouldReturnSuccess_WhenPassengerExists()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        var expectedPassenger = new PassengerDto("PAS-001", "Alice", "Smith", "alice@example.com", "P123", DateTime.UtcNow);

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(expectedPassenger)
            });

        var client = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("http://localhost:5000")
        };

        // Act
        var response = await client.GetAsync("/passengers/PAS-001");
        var result = await response.Content.ReadFromJsonAsync<PassengerDto>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("PAS-001", result.Id);
        Assert.Equal("Alice", result.FirstName);
    }

    [Fact]
    public async Task ValidatePassenger_ShouldReturnNotFound_WhenPassengerDoesNotExist()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound
            });

        var client = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("http://localhost:5000")
        };

        // Act
        var response = await client.GetAsync("/passengers/NON-EXISTENT");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
