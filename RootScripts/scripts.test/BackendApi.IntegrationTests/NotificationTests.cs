using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BackendApi.IntegrationTests;

[Collection("SharedTestDatabase")]
public class NotificationTests : IAsyncLifetime
{
    private readonly DeliveryWebApplicationFactory _factory;
    private HttpClient _client = default!;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public NotificationTests(DeliveryWebApplicationFactory factory)
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
    private record RegisterFcmTokenPayload(string Token, string? DeviceType);
    private record ApiResponseWrapper<T>(bool Success, T? Value, string? Message, string? ErrorDetail);

    // ─── Utility ────────────────────────────────────────────────────
    private async Task<(string Token, string UserId)> RegisterAndGetTokenAndIdAsync(string email)
    {
        var payload = new RegisterPayload(email, "TestPass123!", "Notification User", "Customer");
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var accessToken = doc.RootElement.GetProperty("value").GetProperty("accessToken").GetString();
        var userId = doc.RootElement.GetProperty("value").GetProperty("user").GetProperty("id").GetString();
        return (accessToken!, userId!);
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
    public async Task RegisterNewFcmToken_WithValidData_ReturnsSuccess()
    {
        // Arrange
        var (token, _) = await RegisterAndGetTokenAndIdAsync($"notif_{Guid.NewGuid():N}@test.com");
        var payload = new RegisterFcmTokenPayload("dummy_fcm_token_123", "Android");

        // Act
        var request = CreateAuthRequest(HttpMethod.Post, "/api/v1/Notifications/register-token", token, payload);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponseWrapper<object>>(_jsonOpts);
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.Equal("ลงทะเบียนอุปกรณ์แจ้งเตือนสำเร็จ", body.Message);
    }

    [Fact]
    public async Task FcmTokenReuse_SameTokenWithDifferentUser_UpdatesUserIdCorrectly()
    {
        // Arrange
        var (tokenUserA, userIdA) = await RegisterAndGetTokenAndIdAsync($"notif_usera_{Guid.NewGuid():N}@test.com");
        var (tokenUserB, userIdB) = await RegisterAndGetTokenAndIdAsync($"notif_userb_{Guid.NewGuid():N}@test.com");
        
        var sharedFcmToken = $"shared_fcm_token_{Guid.NewGuid():N}";
        var payload = new RegisterFcmTokenPayload(sharedFcmToken, "iOS");

        // 1. User A registers the token
        var req1 = CreateAuthRequest(HttpMethod.Post, "/api/v1/Notifications/register-token", tokenUserA, payload);
        var res1 = await _client.SendAsync(req1);
        res1.EnsureSuccessStatusCode();

        // 2. User B registers the SAME token
        var req2 = CreateAuthRequest(HttpMethod.Post, "/api/v1/Notifications/register-token", tokenUserB, payload);
        var res2 = await _client.SendAsync(req2);
        res2.EnsureSuccessStatusCode();

        var body2 = await res2.Content.ReadFromJsonAsync<ApiResponseWrapper<object>>(_jsonOpts);
        Assert.NotNull(body2);
        Assert.True(body2.Success);
        Assert.Equal("อัปเดตอุปกรณ์แจ้งเตือนเรียบร้อยแล้ว", body2.Message);
    }
}
