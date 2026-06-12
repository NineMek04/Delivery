using StackExchange.Redis;

namespace BackendApi.Services.Telemetry;

public static class ActiveOrderRecipientCache
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(30);

    private const string SchemaField = "__schema";
    private const string SchemaVersion = "2";
    private const string OrderFieldPrefix = "order:";
    private const string ReplaceScript = """
        redis.call('DEL', KEYS[1])
        for i = 1, #ARGV - 1, 2 do
            redis.call('HSET', KEYS[1], ARGV[i], ARGV[i + 1])
        end
        redis.call('EXPIRE', KEYS[1], ARGV[#ARGV])
        return 1
        """;

    public static string GetKey(string riderId) =>
        $"riders:active_order:{riderId}";

    public static HashEntry[] BuildEntries(
        IEnumerable<KeyValuePair<string, string?>> activeOrders)
    {
        var entries = new List<HashEntry>
        {
            new(SchemaField, SchemaVersion)
        };

        entries.AddRange(
            activeOrders
                .Where(order =>
                    !string.IsNullOrWhiteSpace(order.Key) &&
                    !string.IsNullOrWhiteSpace(order.Value))
                .GroupBy(order => order.Key, StringComparer.Ordinal)
                .Select(group => new HashEntry(
                    $"{OrderFieldPrefix}{group.Key}",
                    group.Last().Value!)));

        return entries.ToArray();
    }

    public static bool TryGetCustomerIds(
        IReadOnlyCollection<HashEntry> entries,
        out IReadOnlyCollection<string> customerIds)
    {
        var hasCurrentSchema = entries.Any(entry =>
            entry.Name == SchemaField &&
            entry.Value == SchemaVersion);

        if (!hasCurrentSchema)
        {
            customerIds = Array.Empty<string>();
            return false;
        }

        customerIds = entries
            .Where(entry =>
                entry.Name.ToString().StartsWith(
                    OrderFieldPrefix,
                    StringComparison.Ordinal) &&
                entry.Value.HasValue)
            .Select(entry => entry.Value.ToString())
            .Where(customerId => !string.IsNullOrWhiteSpace(customerId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return true;
    }

    public static async Task ReplaceAsync(
        IDatabase database,
        string riderId,
        IEnumerable<KeyValuePair<string, string?>> activeOrders)
    {
        var entries = BuildEntries(activeOrders);
        var arguments = new RedisValue[(entries.Length * 2) + 1];

        for (var index = 0; index < entries.Length; index++)
        {
            arguments[index * 2] = entries[index].Name;
            arguments[(index * 2) + 1] = entries[index].Value;
        }

        arguments[^1] = (long)TimeToLive.TotalSeconds;

        await database.ScriptEvaluateAsync(
            ReplaceScript,
            new RedisKey[] { GetKey(riderId) },
            arguments);
    }
}
