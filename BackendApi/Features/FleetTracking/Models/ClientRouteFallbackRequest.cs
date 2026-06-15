using System.ComponentModel.DataAnnotations;

namespace BackendApi.Features.FleetTracking.Models;

public sealed class ClientRouteFallbackRequest
{
    [Required]
    [MaxLength(64)]
    public string OrderId { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(PICKUP|DELIVERY)$")]
    public string RoutePhase { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(MISSING_POLYLINE|INVALID_POLYLINE|LOCAL_OSRM_UNAVAILABLE)$")]
    public string Reason { get; set; } = string.Empty;

    [Range(0, 1_000_000)]
    public int? EncodedLength { get; set; }
}
