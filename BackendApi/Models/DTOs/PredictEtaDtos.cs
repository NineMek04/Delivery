using System.Text.Json.Serialization;

namespace BackendApi.Models.DTOs;

public class PredictEtaRequestDto
{
    [JsonPropertyName("pickup_lat")]
    public double PickupLat { get; set; }

    [JsonPropertyName("pickup_lng")]
    public double PickupLng { get; set; }

    [JsonPropertyName("dropoff_lat")]
    public double DropoffLat { get; set; }

    [JsonPropertyName("dropoff_lng")]
    public double DropoffLng { get; set; }

    [JsonPropertyName("route_distance_meters")]
    public double RouteDistanceMeters { get; set; }

    [JsonPropertyName("route_duration_seconds")]
    public double RouteDurationSeconds { get; set; }

    [JsonPropertyName("current_time")]
    public string? CurrentTime { get; set; }

    [JsonPropertyName("weather_condition")]
    public string? WeatherCondition { get; set; }

    [JsonPropertyName("traffic_level")]
    public string? TrafficLevel { get; set; }

    [JsonPropertyName("rider_speed_kmh")]
    public double? RiderSpeedKmh { get; set; }

    [JsonPropertyName("osrm_pickup_duration_seconds")]
    public double? OsrmPickupDurationSeconds { get; set; }
}

public class PredictEtaResponseDto
{
    [JsonPropertyName("eta_minutes")]
    public double EtaMinutes { get; set; }

    [JsonPropertyName("eta_datetime")]
    public string EtaDatetime { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("factors")]
    public Dictionary<string, object> Factors { get; set; } = new();
}
