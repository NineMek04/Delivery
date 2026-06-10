using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BackendApi.IntegrationTests;

[Collection("SharedTestDatabase")]
public class MenuCategoryTests : IAsyncLifetime
{
    private readonly DeliveryWebApplicationFactory _factory;
    private HttpClient _client = default!;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public MenuCategoryTests(DeliveryWebApplicationFactory factory)
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
    private record CreateShopPayload(string Name, string MenuName, decimal MenuPrice, double Lat, double Lng);
    private record ShopData(string Id, string Name);

    private record CreateMenuCategoryPayload(string Name, string? Description, int DisplayOrder, string ShopId);
    private record UpdateMenuCategoryPayload(string? Name = null, string? Description = null, int? DisplayOrder = null);
    
    private record MenuCategoryData(
        string Id, string TrackingCode, string Name, string? Description,
        int DisplayOrder, string ShopId);

    private record ApiResponseWrapper<T>(bool Success, T? Value, string? Message, string? ErrorDetail);

    // ─── Utility ────────────────────────────────────────────────────
    private async Task<string> RegisterAndGetTokenAsync()
    {
        var email = $"partner_{Guid.NewGuid():N}@test.com";
        var payload = new RegisterPayload(email, "TestPass123!", "Partner Test User", "StorePartner");
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

    private async Task<ShopData> CreateShopAsync(string token)
    {
        var payload = new CreateShopPayload(
            Name: $"Test Shop {Guid.NewGuid():N}",
            MenuName: "Signature Dish",
            MenuPrice: 150.00m,
            Lat: 17.4150,
            Lng: 102.7900
        );
        var request = CreateAuthRequest(HttpMethod.Post, "/api/v1/shops", token, payload);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var wrapper = await response.Content.ReadFromJsonAsync<ApiResponseWrapper<ShopData>>(_jsonOpts);
        return wrapper!.Value!;
    }

    // ─── Tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAndGetCategory_ReturnsOrderedByDisplayOrder()
    {
        // Arrange
        var token = await RegisterAndGetTokenAsync();
        var shop = await CreateShopAsync(token);

        // 1. Create a category with DisplayOrder = 10
        var cat1Payload = new CreateMenuCategoryPayload("Dessert", "Sweet stuff", 10, shop.Id);
        var req1 = CreateAuthRequest(HttpMethod.Post, "/api/v1/MenuCategories", token, cat1Payload);
        var res1 = await _client.SendAsync(req1);
        res1.EnsureSuccessStatusCode();
        
        var w1 = await res1.Content.ReadFromJsonAsync<ApiResponseWrapper<MenuCategoryData>>(_jsonOpts);
        var cat1 = w1!.Value;
        Assert.NotNull(cat1);
        Assert.StartsWith("CAT", cat1.TrackingCode);

        // 2. Create a category with DisplayOrder = 5
        var cat2Payload = new CreateMenuCategoryPayload("Appetizers", "Light bites", 5, shop.Id);
        var req2 = CreateAuthRequest(HttpMethod.Post, "/api/v1/MenuCategories", token, cat2Payload);
        var res2 = await _client.SendAsync(req2);
        res2.EnsureSuccessStatusCode();
        
        var w2 = await res2.Content.ReadFromJsonAsync<ApiResponseWrapper<MenuCategoryData>>(_jsonOpts);
        var cat2 = w2!.Value;
        Assert.NotNull(cat2);

        // 3. Get all categories for this shop
        var reqGet = CreateAuthRequest(HttpMethod.Get, $"/api/v1/MenuCategories/shop/{shop.Id}", token);
        var resGet = await _client.SendAsync(reqGet);
        resGet.EnsureSuccessStatusCode();

        var getBody = await resGet.Content.ReadFromJsonAsync<ApiResponseWrapper<List<MenuCategoryData>>>(_jsonOpts);
        Assert.NotNull(getBody?.Value);
        Assert.True(getBody.Success);

        // Assert they are ordered by DisplayOrder: cat2 (DisplayOrder=5) first, cat1 (DisplayOrder=10) second
        var items = getBody.Value;
        Assert.Equal(2, items.Count);
        Assert.Equal("Appetizers", items[0].Name);
        Assert.Equal("Dessert", items[1].Name);
    }

    [Fact]
    public async Task UpdateCategory_UpdatesAllowedFieldsCorrectly()
    {
        // Arrange
        var token = await RegisterAndGetTokenAsync();
        var shop = await CreateShopAsync(token);

        var createPayload = new CreateMenuCategoryPayload("Main Course", "Heavy meals", 1, shop.Id);
        var reqCreate = CreateAuthRequest(HttpMethod.Post, "/api/v1/MenuCategories", token, createPayload);
        var resCreate = await _client.SendAsync(reqCreate);
        resCreate.EnsureSuccessStatusCode();
        
        var wCreate = await resCreate.Content.ReadFromJsonAsync<ApiResponseWrapper<MenuCategoryData>>(_jsonOpts);
        var cat = wCreate!.Value;

        // Act
        var updatePayload = new UpdateMenuCategoryPayload(
            Name: "Gourmet Main Course",
            Description: "Special chef meals",
            DisplayOrder: 2
        );
        var reqUpdate = CreateAuthRequest(HttpMethod.Put, $"/api/v1/MenuCategories/{cat!.Id}", token, updatePayload);
        var resUpdate = await _client.SendAsync(reqUpdate);
        resUpdate.EnsureSuccessStatusCode();
        
        var wUpdate = await resUpdate.Content.ReadFromJsonAsync<ApiResponseWrapper<MenuCategoryData>>(_jsonOpts);
        var updatedCat = wUpdate!.Value;

        // Assert
        Assert.NotNull(updatedCat);
        Assert.Equal(cat.Id, updatedCat.Id);
        Assert.Equal("Gourmet Main Course", updatedCat.Name);
        Assert.Equal("Special chef meals", updatedCat.Description);
        Assert.Equal(2, updatedCat.DisplayOrder);
    }
}
