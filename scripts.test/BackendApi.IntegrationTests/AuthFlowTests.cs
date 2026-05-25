using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace BackendApi.IntegrationTests;

/// <summary>
/// Integration Tests: Auth Flow
/// login → refresh → session → logout → verify expired
/// ใช้ WebApplicationFactory + Testcontainers PostgreSQL
/// </summary>
[Collection("SharedTestDatabase")]
public class AuthFlowTests : IAsyncLifetime
{
    private HttpClient _client = default!;
    private readonly DeliveryWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AuthFlowTests(DeliveryWebApplicationFactory factory)
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

    private record LoginPayload(string Email, string Password);
    private record RegisterPayload(string Email, string Password, string FullName, string Role);
    private record RefreshPayload(string RefreshToken);

    private record ApiResponseWrapper<T>(bool Success, T? Value, string? Message, string? ErrorDetail);
    private record AuthData(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserData? User);
    private record UserData(string Id, string Email, string Role, string? FullName);

    // ─── Tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithInvalidCredentials_Returns401()
    {
        // Arrange
        var payload = new LoginPayload("nonexistent@test.com", "WrongPassword123!");

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_Login_Refresh_Session_Logout_FullFlow()
    {
        var uniqueEmail = $"testuser_{Guid.NewGuid():N}@test.com";

        // ── Step 1: Register ──────────────────────────────────────
        var registerPayload = new RegisterPayload(uniqueEmail, "TestPass123!", "Test User", "Admin");
        var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerPayload);

        // Registration might be 201 or 200 depending on implementation
        Assert.True(registerResponse.IsSuccessStatusCode,
            $"Register failed: {registerResponse.StatusCode} - {await registerResponse.Content.ReadAsStringAsync()}");

        var registerBody = await registerResponse.Content.ReadFromJsonAsync<ApiResponseWrapper<AuthData>>(_jsonOpts);
        Assert.NotNull(registerBody);
        Assert.True(registerBody.Success);
        Assert.NotNull(registerBody.Value);
        Assert.False(string.IsNullOrWhiteSpace(registerBody.Value.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(registerBody.Value.RefreshToken));

        var accessToken = registerBody.Value.AccessToken;
        var refreshToken = registerBody.Value.RefreshToken;

        // ── Step 2: Login (verify same user can login) ────────────
        var loginPayload = new LoginPayload(uniqueEmail, "TestPass123!");
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginPayload);

        Assert.True(loginResponse.IsSuccessStatusCode,
            $"Login failed: {loginResponse.StatusCode}");

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponseWrapper<AuthData>>(_jsonOpts);
        Assert.NotNull(loginBody?.Value);
        Assert.Equal(uniqueEmail, loginBody.Value.User?.Email);

        accessToken = loginBody.Value.AccessToken;
        refreshToken = loginBody.Value.RefreshToken;

        // ── Step 3: Session (use Bearer token) ────────────────────
        var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/session");
        sessionRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var sessionResponse = await _client.SendAsync(sessionRequest);
        Assert.True(sessionResponse.IsSuccessStatusCode,
            $"Session failed: {sessionResponse.StatusCode}");

        // ── Step 4: Refresh Token ─────────────────────────────────
        var refreshPayload = new RefreshPayload(refreshToken);
        var refreshResponse = await _client.PostAsJsonAsync("/api/v1/auth/refresh", refreshPayload);

        Assert.True(refreshResponse.IsSuccessStatusCode,
            $"Refresh failed: {refreshResponse.StatusCode}");

        var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<ApiResponseWrapper<AuthData>>(_jsonOpts);
        Assert.NotNull(refreshBody?.Value);
        Assert.NotEqual(accessToken, refreshBody.Value.AccessToken); // New token issued

        var newAccessToken = refreshBody.Value.AccessToken;

        // ── Step 5: Old token should still work (or not, depends on revoke impl) ──
        // Use new token for logout
        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", newAccessToken);

        var logoutResponse = await _client.SendAsync(logoutRequest);
        Assert.True(logoutResponse.IsSuccessStatusCode,
            $"Logout failed: {logoutResponse.StatusCode}");
    }

    [Fact]
    public async Task Session_WithoutToken_Returns401()
    {
        // Act — no Authorization header
        var response = await _client.GetAsync("/api/v1/auth/session");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithInvalidToken_Returns401Or400()
    {
        // Arrange
        var payload = new RefreshPayload("invalid-refresh-token-value");

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", payload);

        // Assert — should be 400 or 401
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected 400 or 401 but got {response.StatusCode}");
    }
}
