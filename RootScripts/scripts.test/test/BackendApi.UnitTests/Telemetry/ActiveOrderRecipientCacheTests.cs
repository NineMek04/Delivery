using BackendApi.Services.Telemetry;
using StackExchange.Redis;

namespace BackendApi.UnitTests.Telemetry;

public class ActiveOrderRecipientCacheTests
{
    [Fact]
    public void BuildEntries_PreservesEveryActiveOrder()
    {
        var entries = ActiveOrderRecipientCache.BuildEntries(
            new[]
            {
                new KeyValuePair<string, string?>("order-1", "customer-1"),
                new KeyValuePair<string, string?>("order-2", "customer-2"),
                new KeyValuePair<string, string?>("order-3", "customer-1")
            });

        Assert.Contains(entries, entry =>
            entry.Name == "__schema" && entry.Value == "2");
        Assert.Contains(entries, entry =>
            entry.Name == "order:order-1" && entry.Value == "customer-1");
        Assert.Contains(entries, entry =>
            entry.Name == "order:order-2" && entry.Value == "customer-2");
        Assert.Contains(entries, entry =>
            entry.Name == "order:order-3" && entry.Value == "customer-1");
    }

    [Fact]
    public void TryGetCustomerIds_DeduplicatesCustomers()
    {
        var entries = ActiveOrderRecipientCache.BuildEntries(
            new[]
            {
                new KeyValuePair<string, string?>("order-1", "customer-1"),
                new KeyValuePair<string, string?>("order-2", "customer-2"),
                new KeyValuePair<string, string?>("order-3", "customer-1")
            });

        var isCurrent = ActiveOrderRecipientCache.TryGetCustomerIds(
            entries,
            out var customerIds);

        Assert.True(isCurrent);
        Assert.Equal(
            new[] { "customer-1", "customer-2" },
            customerIds.OrderBy(customerId => customerId));
    }

    [Fact]
    public void TryGetCustomerIds_RejectsLegacySingleCustomerSchema()
    {
        var legacyEntries = new[]
        {
            new HashEntry("order_id", "order-1"),
            new HashEntry("customer_id", "customer-1")
        };

        var isCurrent = ActiveOrderRecipientCache.TryGetCustomerIds(
            legacyEntries,
            out var customerIds);

        Assert.False(isCurrent);
        Assert.Empty(customerIds);
        Assert.Equal(
            TimeSpan.FromSeconds(30),
            ActiveOrderRecipientCache.TimeToLive);
    }

    [Fact]
    public void BuildEntries_WithNoOrders_CreatesCurrentEmptyCache()
    {
        var entries = ActiveOrderRecipientCache.BuildEntries(
            Array.Empty<KeyValuePair<string, string?>>());

        var isCurrent = ActiveOrderRecipientCache.TryGetCustomerIds(
            entries,
            out var customerIds);

        Assert.True(isCurrent);
        Assert.Empty(customerIds);
        Assert.Single(entries);
    }
}
