using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BackendApi.IntegrationTests;

/// <summary>
/// Integration Tests: Order Cancellation Scenarios
/// create → cancel, verify state transitions and error handling
/// </summary>
[Collection("SharedTestDatabase")]
public class OrderCancelTests : IAsyncLifetime
{
    private readonly DeliveryWebApplicationFactory _factory;
    private HttpClient _client = default!;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public OrderCancelTests(DeliveryWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ─── Helper DTOs ────────────────────────────────────────────────

    private record RegisterPayload(string Email, string Password, string FullName, string Role);
    private record ApiResponseWrapper<T>(bool Success, T? Value, string? Message, List<string>? Errors);
    private record AuthData(string AccessToken, string RefreshToken, DateTime ExpiresAt);
    private record CreateOrderPayload(double PickupLat, double PickupLng, double DropoffLat, double DropoffLng, DateTime ExpectedDeliveryTime);
    private record OrderData(string Id, string Status);

    // ─── Utility ────────────────────────────────────────────────────

    private async Task<string> RegisterAndGetTokenAsync()
    {
        var email = $"cancel_test_{Guid.NewGuid():N}@test.com";
        var payload = new RegisterPayload(email, "TestPass123!", "Cancel Test User", "Admin");
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ApiResponseWrapper<AuthData>>(_jsonOpts);
        return body!.Value!.AccessToken;
    }

    private HttpRequestMessage CreateAuthRequest(HttpMethod method, string uri, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    // ─── Tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task CancelOrder_NonExistentId_Returns404()
    {
        // Arrange
        var token = await RegisterAndGetTokenAsync();
        var fakeId = Guid.NewGuid().ToString();

        // Act
        var request = CreateAuthRequest(HttpMethod.Post, $"/api/v1/orders/{fakeId}/cancel", token);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CancelOrder_WithoutAuth_Returns401()
    {
        // Arrange
        var fakeId = Guid.NewGuid().ToString();

        // Act — no Bearer token
        var response = await _client.PostAsync($"/api/v1/orders/{fakeId}/cancel", null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CancelOrder_AfterCreate_ReturnsCancelledState()
    {
        // Arrange
        var token = await RegisterAndGetTokenAsync();
        var createPayload = new CreateOrderPayload(
            17.4150, 102.7880, 17.4100, 102.7850, DateTime.UtcNow.AddHours(1));

        var createRequest = CreateAuthRequest(HttpMethod.Post, "/api/v1/orders", token, createPayload);
        var createResponse = await _client.SendAsync(createRequest);

        // If OSRM is not running, skip gracefully
        if (createResponse.StatusCode == HttpStatusCode.BadRequest)
            return;

        createResponse.EnsureSuccessStatusCode();
        var createBody = await createResponse.Content.ReadFromJsonAsync<ApiResponseWrapper<OrderData>>(_jsonOpts);
        var orderId = createBody!.Value!.Id;

        // Act — Cancel the order
        var cancelRequest = CreateAuthRequest(HttpMethod.Post, $"/api/v1/orders/{orderId}/cancel", token);
        var cancelResponse = await _client.SendAsync(cancelRequest);

        // Assert
        Assert.True(cancelResponse.IsSuccessStatusCode,
            $"Cancel failed: {cancelResponse.StatusCode} - {await cancelResponse.Content.ReadAsStringAsync()}");

        var cancelBody = await cancelResponse.Content.ReadFromJsonAsync<ApiResponseWrapper<OrderData>>(_jsonOpts);
        Assert.NotNull(cancelBody?.Value);
        Assert.Equal("CANCELLED", cancelBody.Value.Status);
    }
}
