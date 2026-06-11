using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.IntegrationTests;

[Collection("SharedTestDatabase")]
public class SecurityAuthorizationTests : IAsyncLifetime
{
    private readonly DeliveryWebApplicationFactory _factory;
    private HttpClient _client = default!;

    public SecurityAuthorizationTests(DeliveryWebApplicationFactory factory)
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
    public async Task Riders_WithoutAuthentication_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/riders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthDetail_WithoutOperationsRole_Returns401()
    {
        var response = await _client.GetAsync("/health/detail");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrderById_AsDifferentCustomer_Returns403()
    {
        var owner = await RegisterAsync("Customer");
        var attacker = await RegisterAsync("Customer");
        var geometryFactory = NetTopologySuite.NtsGeometryServices.Instance
            .CreateGeometryFactory(srid: 4326);
        var order = new Order
        {
            CustomerId = owner.UserId,
            State = OrderState.CREATED,
            PickupLocation = geometryFactory.CreatePoint(
                new NetTopologySuite.Geometries.Coordinate(102.79, 17.41)),
            DropoffLocation = geometryFactory.CreatePoint(
                new NetTopologySuite.Geometries.Coordinate(102.78, 17.40)),
            ExpectedDeliveryTime = DateTime.UtcNow.AddHours(1)
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/v1/orders/{order.Id}",
            attacker.Token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StorePartner_CannotCreateMenuForAnotherShop()
    {
        var owner = await RegisterAsync("StorePartner");
        var attacker = await RegisterAsync("StorePartner");

        var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/v1/menuitems",
            attacker.Token,
            new
            {
                Name = "Unauthorized item",
                Price = 10m,
                ShopId = owner.ShopId
            });
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StorePartner_CannotCreateAdditionalShop()
    {
        var storePartner = await RegisterAsync("StorePartner");
        var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/v1/shops",
            storePartner.Token,
            new
            {
                Name = "Unauthorized shop",
                MenuName = "Menu",
                MenuPrice = 10m,
                Lat = 17.41,
                Lng = 102.79
            });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Token_WithChangedRole_IsRejected()
    {
        var (token, userId) = await _factory.CreatePrivilegedUserAndGetTokenAsync(
            _client,
            "Admin");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.Id == userId);
            user.Role = "Customer";
            await db.SaveChangesAsync();
        }

        var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/orders", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<(string Token, string UserId, string? ShopId)> RegisterAsync(string role)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                Email = $"security_{Guid.NewGuid():N}@test.com",
                Password = "TestPass123!",
                FullName = "Security Test User",
                Role = role
            });
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var value = document.RootElement.GetProperty("value");
        var user = value.GetProperty("user");
        return (
            value.GetProperty("accessToken").GetString()!,
            user.GetProperty("id").GetString()!,
            user.TryGetProperty("shopId", out var shopId) && shopId.ValueKind != JsonValueKind.Null
                ? shopId.GetString()
                : null);
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string uri,
        string token,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }
}
