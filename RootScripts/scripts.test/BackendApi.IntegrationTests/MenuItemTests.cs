using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BackendApi.IntegrationTests;

[Collection("SharedTestDatabase")]
public class MenuItemTests : IAsyncLifetime
{
    private readonly DeliveryWebApplicationFactory _factory;
    private HttpClient _client = default!;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public MenuItemTests(DeliveryWebApplicationFactory factory)
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

    private record CreateMenuItemPayload(
        string Name,
        string? Description,
        decimal Price,
        string? ImageUrl,
        string ShopId,
        string? MenuCategoryId = null
    );

    private record MenuItemData(
        string Id,
        string TrackingCode,
        string Name,
        string? Description,
        decimal Price,
        string? ImageUrl,
        string ShopId
    );

    private record PaginatedResultWrapper<T>(List<T> Items, int TotalCount, int Page, int PageSize);
    private record ApiResponseWrapper<T>(bool Success, T? Value, string? Message, string? ErrorDetail);

    // ─── Utility ────────────────────────────────────────────────────
    private async Task<(string Token, string ShopId)> RegisterAndGetTokenAsync()
    {
        var email = $"partner_{Guid.NewGuid():N}@test.com";
        var payload = new RegisterPayload(email, "TestPass123!", "Partner Test User", "StorePartner");
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var value = doc.RootElement.GetProperty("value");
        var token = value.GetProperty("accessToken").GetString();
        var shopId = value.GetProperty("user").GetProperty("shopId").GetString();
        return (token!, shopId!);
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
    public async Task CreateAndDeleteMenuItem_Succeeds_AndFiltersFromGet()
    {
        // Arrange
        var (token, shopId) = await RegisterAndGetTokenAsync();
        var shop = new ShopData(shopId, "Partner Test User");

        // 1. Create a menu item
        var createPayload = new CreateMenuItemPayload(
            Name: "กระเพราหมูกรอบ",
            Description: "เผ็ดสะใจหมูกรอบเน้นๆ",
            Price: 85.00m,
            ImageUrl: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=",
            ShopId: shop.Id
        );

        var reqCreate = CreateAuthRequest(HttpMethod.Post, "/api/v1/menuitems", token, createPayload);
        var resCreate = await _client.SendAsync(reqCreate);
        Assert.True(resCreate.IsSuccessStatusCode, $"Create failed: {resCreate.StatusCode} - {await resCreate.Content.ReadAsStringAsync()}");

        var wCreate = await resCreate.Content.ReadFromJsonAsync<ApiResponseWrapper<MenuItemData>>(_jsonOpts);
        Assert.NotNull(wCreate?.Value);
        var menuItem = wCreate.Value;
        Assert.Equal("กระเพราหมูกรอบ", menuItem.Name);

        // 2. Get menu items for shop (should return the created item)
        var reqGetBefore = CreateAuthRequest(HttpMethod.Get, $"/api/v1/menuitems/shop/{shop.Id}?page=1&pageSize=20", token);
        var resGetBefore = await _client.SendAsync(reqGetBefore);
        resGetBefore.EnsureSuccessStatusCode();
        var wGetBefore = await resGetBefore.Content.ReadFromJsonAsync<ApiResponseWrapper<PaginatedResultWrapper<MenuItemData>>>(_jsonOpts);
        Assert.NotNull(wGetBefore?.Value);
        Assert.Single(wGetBefore.Value.Items);
        Assert.Equal(menuItem.Id, wGetBefore.Value.Items[0].Id);

        // 3. Delete the menu item
        var reqDelete = CreateAuthRequest(HttpMethod.Delete, $"/api/v1/menuitems/{menuItem.Id}", token);
        var resDelete = await _client.SendAsync(reqDelete);
        Assert.Equal(HttpStatusCode.NoContent, resDelete.StatusCode);

        // 4. Get menu items for shop again (should be empty now)
        var reqGetAfter = CreateAuthRequest(HttpMethod.Get, $"/api/v1/menuitems/shop/{shop.Id}?page=1&pageSize=20", token);
        var resGetAfter = await _client.SendAsync(reqGetAfter);
        resGetAfter.EnsureSuccessStatusCode();
        var wGetAfter = await resGetAfter.Content.ReadFromJsonAsync<ApiResponseWrapper<PaginatedResultWrapper<MenuItemData>>>(_jsonOpts);
        Assert.NotNull(wGetAfter?.Value);
        Assert.Empty(wGetAfter.Value.Items);
    }
}
