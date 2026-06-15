using BackendApi.Services.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BackendApi.IntegrationTests;

[Collection("SharedTestDatabase")]
public class ActiveOrderRecipientCacheIntegrationTests
{
    private readonly DeliveryWebApplicationFactory _factory;

    public ActiveOrderRecipientCacheIntegrationTests(
        DeliveryWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReplaceAsync_StoresAllRecipientsWithShortTtl()
    {
        using var scope = _factory.Services.CreateScope();
        var redis = scope.ServiceProvider
            .GetRequiredService<IConnectionMultiplexer>();
        var database = redis.GetDatabase();
        var riderId = $"cache-test-{Guid.NewGuid():N}";
        var key = ActiveOrderRecipientCache.GetKey(riderId);

        try
        {
            await ActiveOrderRecipientCache.ReplaceAsync(
                database,
                riderId,
                new[]
                {
                    new KeyValuePair<string, string?>(
                        "order-1",
                        "customer-1"),
                    new KeyValuePair<string, string?>(
                        "order-2",
                        "customer-2")
                });

            var entries = await database.HashGetAllAsync(key);
            var isCurrent = ActiveOrderRecipientCache.TryGetCustomerIds(
                entries,
                out var customerIds);
            var ttl = await database.KeyTimeToLiveAsync(key);

            Assert.True(isCurrent);
            Assert.Equal(
                new[] { "customer-1", "customer-2" },
                customerIds.OrderBy(customerId => customerId));
            Assert.NotNull(ttl);
            Assert.InRange(
                ttl!.Value,
                TimeSpan.FromSeconds(1),
                ActiveOrderRecipientCache.TimeToLive);
        }
        finally
        {
            await database.KeyDeleteAsync(key);
        }
    }
}
