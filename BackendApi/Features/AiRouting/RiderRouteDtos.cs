using System.ComponentModel.DataAnnotations;

namespace BackendApi.Features.AiRouting;

public sealed class RiderRouteRequest
{
    [Required]
    [MaxLength(64)]
    public string OrderId { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(PICKUP|DELIVERY)$")]
    public string RoutePhase { get; set; } = string.Empty;

    [Range(-90.0, 90.0)]
    public double CurrentLat { get; set; }

    [Range(-180.0, 180.0)]
    public double CurrentLng { get; set; }
}

public sealed class RiderRouteResponse
{
    public string EncodedPolyline { get; set; } = string.Empty;
    public double DistanceMeters { get; set; }
    public double DurationSeconds { get; set; }
    public string Source { get; set; } = "LOCAL_OSRM";
}
