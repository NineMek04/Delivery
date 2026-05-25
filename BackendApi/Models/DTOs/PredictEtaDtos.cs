namespace BackendApi.Models.DTOs;

public class PredictEtaRequestDto
{
    public double PickupLat { get; set; }
    public double PickupLng { get; set; }
    public double DropoffLat { get; set; }
    public double DropoffLng { get; set; }
    public double RouteDistanceMeters { get; set; }
    public double RouteDurationSeconds { get; set; }
    public string? CurrentTime { get; set; }
    public string? WeatherCondition { get; set; }
    public string? TrafficLevel { get; set; }
}

public class PredictEtaResponseDto
{
    public double EtaMinutes { get; set; }
    public string EtaDatetime { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public Dictionary<string, object> Factors { get; set; } = new();
}
