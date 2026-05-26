using System.Text.Json.Serialization;

namespace BackendApi.Models.DTOs;

// --- Dispatch Ranking Models ---

public class DispatchContextDto
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;
}

public class DispatchOrderDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("pickup")]
    public List<double> Pickup { get; set; } = new();

    [JsonPropertyName("dropoff")]
    public List<double> Dropoff { get; set; } = new();

    [JsonPropertyName("sla_limit_minutes")]
    public int SlaLimitMinutes { get; set; } = 30;
}

public class DispatchCandidateDto
{
    [JsonPropertyName("rider_id")]
    public string RiderId { get; set; } = string.Empty;

    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lng")]
    public double Lng { get; set; }

    [JsonPropertyName("speed_kmh")]
    public double SpeedKmh { get; set; }

    [JsonPropertyName("current_tasks")]
    public List<Dictionary<string, object>> CurrentTasks { get; set; } = new();
}

public class DispatchRankRequestDto
{
    [JsonPropertyName("context")]
    public DispatchContextDto Context { get; set; } = new();

    [JsonPropertyName("order")]
    public DispatchOrderDto Order { get; set; } = new();

    [JsonPropertyName("candidates")]
    public List<DispatchCandidateDto> Candidates { get; set; } = new();
}

public class RankedCandidateDto
{
    [JsonPropertyName("rider_id")]
    public string RiderId { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("distance_to_pickup_km")]
    public double DistanceToPickupKm { get; set; }

    [JsonPropertyName("eta_minutes")]
    public int EtaMinutes { get; set; }
}

public class DispatchRankResponseDto
{
    [JsonPropertyName("ranked_candidates")]
    public List<RankedCandidateDto> RankedCandidates { get; set; } = new();
}


// --- Routing Models ---

public class LocationDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lng")]
    public double Lng { get; set; }
}

public class RoutingRequestDto
{
    [JsonPropertyName("locations")]
    public List<LocationDto> Locations { get; set; } = new();

    [JsonPropertyName("num_vehicles")]
    public int NumVehicles { get; set; } = 1;

    [JsonPropertyName("depot")]
    public int Depot { get; set; } = 0;
}

public class RouteWaypointDto
{
    [JsonPropertyName("sequence")]
    public int Sequence { get; set; }

    [JsonPropertyName("location_id")]
    public string LocationId { get; set; } = string.Empty;

    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lng")]
    public double Lng { get; set; }
}

public class RoutingResponseDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("optimized_route")]
    public List<RouteWaypointDto>? OptimizedRoute { get; set; }

    [JsonPropertyName("total_distance_meters")]
    public double? TotalDistanceMeters { get; set; }
}
