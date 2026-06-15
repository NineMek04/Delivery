using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BackendApi.IntegrationTests;

[Collection("SharedTestDatabase")]
public class CustomerAddressTests : IAsyncLifetime
{
    private readonly DeliveryWebApplicationFactory _factory;
    private HttpClient _client = default!;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public CustomerAddressTests(DeliveryWebApplicationFactory factory)
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
    private record ApiResponseWrapper<T>(bool Success, T? Value, string? Message, string? ErrorDetail);
    
    private record CreateAddressPayload(
        string Name, string AddressLine1, string? AddressLine2,
        string City, string State, string PostalCode,
        double Latitude, double Longitude, bool IsDefault);

    private record UpdateAddressPayload(
        string? Name = null, string? AddressLine1 = null, string? AddressLine2 = null,
        string? City = null, string? State = null, string? PostalCode = null,
        double? Latitude = null, double? Longitude = null, bool? IsDefault = null);

    private record AddressData(
        string Id, string TrackingCode, string UserId, string Name,
        string AddressLine1, string? AddressLine2, string City, string State,
        string PostalCode, double Latitude, double Longitude, bool IsDefault);

    private record PaginatedAddresses(
        List<AddressData> Items, int TotalCount, int Page, int PageSize);

    // ─── Utility ────────────────────────────────────────────────────
    private async Task<string> RegisterAndGetTokenAsync(string email)
    {
        var payload = new RegisterPayload(email, "TestPass123!", "Customer Test User", "Customer");
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var token = doc.RootElement.GetProperty("value").GetProperty("accessToken").GetString();
        return token!;
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
    public async Task CreateAddress_WithValidData_ReturnsCreated()
    {
        // Arrange
        var token = await RegisterAndGetTokenAsync($"cust_{Guid.NewGuid():N}@test.com");
        var payload = new CreateAddressPayload(
            Name: "Home Office",
            AddressLine1: "123 Delivery Road",
            AddressLine2: "Floor 2",
            City: "Udon Thani",
            State: "Mueang Udon Thani",
            PostalCode: "41000",
            Latitude: 17.4138,
            Longitude: 102.7872,
            IsDefault: true
        );

        // Act
        var request = CreateAuthRequest(HttpMethod.Post, "/api/v1/CustomerAddresses", token, payload);
        var response = await _client.SendAsync(request);

        // Assert
        Assert.True(response.IsSuccessStatusCode, $"CreateAddress failed: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
        var wrapper = await response.Content.ReadFromJsonAsync<ApiResponseWrapper<AddressData>>(_jsonOpts);
        Assert.NotNull(wrapper);
        Assert.True(wrapper.Success);
        
        var address = wrapper.Value;
        Assert.NotNull(address);
        Assert.False(string.IsNullOrWhiteSpace(address.Id));
        Assert.Equal("Home Office", address.Name);
        Assert.Equal(17.4138, address.Latitude);
        Assert.Equal(102.7872, address.Longitude);
        Assert.True(address.IsDefault);
        Assert.StartsWith("ADR", address.TrackingCode);
    }

    [Fact]
    public async Task ResetOtherDefaultAddresses_WhenNewAddressIsDefault_FlipsOldAddressIsDefaultToFalse()
    {
        // Arrange
        var token = await RegisterAndGetTokenAsync($"cust_{Guid.NewGuid():N}@test.com");
        
        // 1. Create first default address
        var address1Payload = new CreateAddressPayload("First Default", "Addr 1", null, "City", "State", "10000", 17.0, 102.0, IsDefault: true);
        var req1 = CreateAuthRequest(HttpMethod.Post, "/api/v1/CustomerAddresses", token, address1Payload);
        var res1 = await _client.SendAsync(req1);
        res1.EnsureSuccessStatusCode();
        var w1 = await res1.Content.ReadFromJsonAsync<ApiResponseWrapper<AddressData>>(_jsonOpts);
        var addr1 = w1!.Value;
        Assert.True(addr1!.IsDefault);

        // 2. Create second default address
        var address2Payload = new CreateAddressPayload("Second Default", "Addr 2", null, "City", "State", "10000", 17.1, 102.1, IsDefault: true);
        var req2 = CreateAuthRequest(HttpMethod.Post, "/api/v1/CustomerAddresses", token, address2Payload);
        var res2 = await _client.SendAsync(req2);
        res2.EnsureSuccessStatusCode();
        var w2 = await res2.Content.ReadFromJsonAsync<ApiResponseWrapper<AddressData>>(_jsonOpts);
        var addr2 = w2!.Value;
        Assert.True(addr2!.IsDefault);

        // 3. Fetch first address and verify it is no longer default
        var reqGet1 = CreateAuthRequest(HttpMethod.Get, $"/api/v1/CustomerAddresses/{addr1.Id}", token);
        var resGet1 = await _client.SendAsync(reqGet1);
        resGet1.EnsureSuccessStatusCode();
        var wGet1 = await resGet1.Content.ReadFromJsonAsync<ApiResponseWrapper<AddressData>>(_jsonOpts);
        var addr1Updated = wGet1!.Value;
        
        Assert.False(addr1Updated!.IsDefault);
    }

    [Fact]
    public async Task TenantIsolation_CannotGetOrUpdateOrDeleteOtherUserAddress()
    {
        // Arrange
        var tokenUserA = await RegisterAndGetTokenAsync($"usera_{Guid.NewGuid():N}@test.com");
        var tokenUserB = await RegisterAndGetTokenAsync($"userb_{Guid.NewGuid():N}@test.com");

        // 1. User A creates an address
        var payload = new CreateAddressPayload("User A Home", "Addr A", null, "City", "State", "10000", 17.0, 102.0, IsDefault: true);
        var reqCreate = CreateAuthRequest(HttpMethod.Post, "/api/v1/CustomerAddresses", tokenUserA, payload);
        var resCreate = await _client.SendAsync(reqCreate);
        resCreate.EnsureSuccessStatusCode();
        var wCreate = await resCreate.Content.ReadFromJsonAsync<ApiResponseWrapper<AddressData>>(_jsonOpts);
        var addressA = wCreate!.Value;

        // 2. User B tries to GET User A's address -> Expected 403 Forbidden
        var reqGet = CreateAuthRequest(HttpMethod.Get, $"/api/v1/CustomerAddresses/{addressA!.Id}", tokenUserB);
        var resGet = await _client.SendAsync(reqGet);
        Assert.Equal(HttpStatusCode.Forbidden, resGet.StatusCode);

        // 3. User B tries to UPDATE User A's address -> Expected 403 Forbidden
        var updatePayload = new UpdateAddressPayload(Name: "Hacked Name");
        var reqUpdate = CreateAuthRequest(HttpMethod.Put, $"/api/v1/CustomerAddresses/{addressA.Id}", tokenUserB, updatePayload);
        var resUpdate = await _client.SendAsync(reqUpdate);
        Assert.Equal(HttpStatusCode.Forbidden, resUpdate.StatusCode);

        // 4. User B tries to DELETE User A's address -> Expected 403 Forbidden
        var reqDelete = CreateAuthRequest(HttpMethod.Delete, $"/api/v1/CustomerAddresses/{addressA.Id}", tokenUserB);
        var resDelete = await _client.SendAsync(reqDelete);
        Assert.Equal(HttpStatusCode.Forbidden, resDelete.StatusCode);
    }
}
