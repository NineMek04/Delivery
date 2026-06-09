using System.Text.Json.Serialization;

namespace BackendApi.Infrastructure.EventBus;

/// <summary>
/// Base class for all integration events emitted across service boundaries.
/// </summary>
public record IntegrationEvent
{
    [JsonPropertyName("Id")]
    public Guid Id { get; init; }

    [JsonPropertyName("CreationDate")]
    public DateTime CreationDate { get; init; }

    [JsonPropertyName("CorrelationId")]
    public string? CorrelationId { get; init; }

    public IntegrationEvent()
    {
        Id = Guid.NewGuid();
        CreationDate = DateTime.UtcNow;
    }

    public IntegrationEvent(string? correlationId) : this(Guid.NewGuid(), DateTime.UtcNow, correlationId)
    {
    }

    [JsonConstructor]
    public IntegrationEvent(Guid id, DateTime creationDate, string? correlationId = null)
    {
        Id = id;
        CreationDate = creationDate;
        CorrelationId = correlationId;
    }
}
