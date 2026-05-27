using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace BackendApi.IntegrationTests;

[Collection("SharedTestDatabase")]
public class CorrelationIdTests : IAsyncLifetime
{
    private HttpClient _client = default!;
    private readonly DeliveryWebApplicationFactory _factory;

    public CorrelationIdTests(DeliveryWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Request_WithoutCorrelationId_GeneratesAndReturnsCorrelationId()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/menu/categories");

        // Assert
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        var correlationId = response.Headers.GetValues("X-Correlation-Id").FirstOrDefault();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [Fact]
    public async Task Request_WithCorrelationId_HonorsAndReturnsSameCorrelationId()
    {
        // Arrange
        var customId = $"custom-correlation-{Guid.NewGuid():N}";
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/menu/categories");
        request.Headers.Add("X-Correlation-Id", customId);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        var returnedId = response.Headers.GetValues("X-Correlation-Id").FirstOrDefault();
        Assert.Equal(customId, returnedId);
    }
}
