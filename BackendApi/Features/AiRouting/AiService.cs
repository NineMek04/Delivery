using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackendApi.Models.DTOs;

namespace BackendApi.Services.Ai;

public class AiService : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiService> _logger;

    /// <summary>
    /// [SNAKE_CASE FIX] Python/Pydantic endpoints expect snake_case field names
    /// (pickup_lat, rider_speed_kmh, etc.). Default System.Text.Json serialises
    /// with camelCase, which Pydantic rejects with 422 when extra="forbid".
    /// Using SnakeCaseLower here ensures all PostAsJsonAsync / ReadFromJsonAsync
    /// calls for this client use the correct naming convention without touching
    /// any other HttpClient in the system.
    /// </summary>
    private static readonly JsonSerializerOptions _snakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true   // tolerant deserialization
    };

    public AiService(HttpClient httpClient, ILogger<AiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<DispatchRankResponseDto?> RankDispatchCandidatesAsync(DispatchRankRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/v1/dispatch/rank", request, _snakeCaseOptions, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to rank candidates. Status: {Status}, Body: {Body}", response.StatusCode, errorBody);
                return GenerateFallbackResponse(request);
            }

            var result = await response.Content.ReadFromJsonAsync<DispatchRankResponseDto>(_snakeCaseOptions, cancellationToken);
            return result ?? GenerateFallbackResponse(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while calling AI Engine for dispatch ranking. Falling back to rule-based Haversine ranking.");
            return GenerateFallbackResponse(request);
        }
    }

    public async Task<RoutingResponseDto?> OptimizeRouteAsync(RoutingRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/optimize-route", request, _snakeCaseOptions, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to optimize route. Status: {Status}, Body: {Body}", response.StatusCode, errorBody);
                return GenerateFallbackRoutingResponse(request);
            }

            var result = await response.Content.ReadFromJsonAsync<RoutingResponseDto>(_snakeCaseOptions, cancellationToken);
            return result ?? GenerateFallbackRoutingResponse(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while calling AI Engine for route optimization. Falling back to Nearest-Neighbor TSP heuristic.");
            return GenerateFallbackRoutingResponse(request);
        }
    }

    public async Task<PredictEtaResponseDto?> PredictEtaAsync(PredictEtaRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/v1/predict-eta", request, _snakeCaseOptions, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to predict ETA. Status: {Status}, Body: {Body}", response.StatusCode, errorBody);
                return GenerateFallbackEtaResponse(request);
            }

            var result = await response.Content.ReadFromJsonAsync<PredictEtaResponseDto>(_snakeCaseOptions, cancellationToken);
            return result ?? GenerateFallbackEtaResponse(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while calling AI Engine for ETA prediction. Falling back to rule-based speed/traffic ETA estimation.");
            return GenerateFallbackEtaResponse(request);
        }
    }

    #region Fallback Mechanisms
    private double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2) =>
        BackendApi.Core.Helpers.GeoMath.HaversineDistanceKm(lat1, lon1, lat2, lon2);

    private DispatchRankResponseDto GenerateFallbackResponse(DispatchRankRequestDto request)
    {
        var pickupLat = request.Order.Pickup.Count > 0 ? request.Order.Pickup[0] : 0;
        var pickupLng = request.Order.Pickup.Count > 1 ? request.Order.Pickup[1] : 0;

        var ranked = new List<RankedCandidateDto>();
        foreach (var c in request.Candidates)
        {
            var distance = CalculateHaversineDistance(c.Lat, c.Lng, pickupLat, pickupLng);
            var score = distance * 5.0; // Lower = Better (align with AI Engine)
            var eta = (int)Math.Max(1.0, Math.Ceiling(distance * 3.0));

            ranked.Add(new RankedCandidateDto
            {
                RiderId = c.RiderId,
                DistanceToPickupKm = distance,
                Score = score,
                EtaMinutes = eta
            });
        }

        return new DispatchRankResponseDto
        {
            RankedCandidates = ranked.OrderBy(r => r.Score).ToList()
        };
    }

    private RoutingResponseDto GenerateFallbackRoutingResponse(RoutingRequestDto request)
    {
        var unvisited = request.Locations.Select((loc, idx) => (loc, idx)).ToList();
        var route = new List<RouteWaypointDto>();
        
        var depotIdx = request.Depot;
        if (depotIdx < 0 || depotIdx >= request.Locations.Count)
        {
            depotIdx = 0;
        }

        var current = unvisited.FirstOrDefault(x => x.idx == depotIdx);
        if (current.loc != null)
        {
            route.Add(new RouteWaypointDto
            {
                Sequence = 0,
                LocationId = current.loc.Id,
                Lat = current.loc.Lat,
                Lng = current.loc.Lng
            });
            unvisited.Remove(current);
        }

        int sequence = 1;
        double totalDistance = 0;

        while (unvisited.Any())
        {
            var lastWaypoint = route.Last();
            var nearest = unvisited
                .Select(item => new { item.loc, item.idx, dist = CalculateHaversineDistance(lastWaypoint.Lat, lastWaypoint.Lng, item.loc.Lat, item.loc.Lng) })
                .OrderBy(item => item.dist)
                .First();

            route.Add(new RouteWaypointDto
            {
                Sequence = sequence++,
                LocationId = nearest.loc.Id,
                Lat = nearest.loc.Lat,
                Lng = nearest.loc.Lng
            });
            totalDistance += nearest.dist * 1000;
            unvisited.RemoveAll(x => x.idx == nearest.idx);
        }

        return new RoutingResponseDto
        {
            Status = "SUCCESS",
            Message = "Fallback Nearest-Neighbor Route Optimization",
            OptimizedRoute = route,
            TotalDistanceMeters = totalDistance
        };
    }

    private PredictEtaResponseDto GenerateFallbackEtaResponse(PredictEtaRequestDto request)
    {
        var currentTime = DateTimeOffset.TryParse(request.CurrentTime, out var parsedCurrentTime)
            ? parsedCurrentTime
            : DateTimeOffset.UtcNow;
        var baseSeconds = request.RouteDurationSeconds > 0
            ? request.RouteDurationSeconds
            : 15 * 60.0;

        var trafficMultiplier = request.TrafficLevel?.ToLowerInvariant() switch
        {
            "heavy" or "high" => 1.5,
            "medium" => 1.25,
            "light" => 0.9,
            _ => 1.0
        };

        if ((currentTime.Hour >= 7 && currentTime.Hour <= 9) ||
            (currentTime.Hour >= 17 && currentTime.Hour <= 19))
        {
            trafficMultiplier = Math.Max(trafficMultiplier, 1.3);
        }

        var weatherMultiplier = request.WeatherCondition?.ToLowerInvariant() switch
        {
            "rain" or "rainy" => 1.4,
            "storm" => 1.8,
            _ => 1.0
        };

        var velocityFactor = 1.0;
        if (request.RiderSpeedKmh is > 0 && request.RouteDurationSeconds > 0)
        {
            var osrmAssumedSpeed =
                (request.RouteDistanceMeters / 1000.0) /
                (request.RouteDurationSeconds / 3600.0);
            velocityFactor = Math.Clamp(osrmAssumedSpeed / request.RiderSpeedKmh.Value, 0.5, 3.0);
        }

        var dispatchPickupSeconds = request.OsrmPickupDurationSeconds is > 0
            ? request.OsrmPickupDurationSeconds.Value + 120
            : 600;
        const double dropoffSeconds = 180;
        var adjustedTravelSeconds =
            baseSeconds * trafficMultiplier * weatherMultiplier * velocityFactor;
        var totalSeconds = adjustedTravelSeconds + dispatchPickupSeconds + dropoffSeconds;
        var etaMinutes = Math.Ceiling(totalSeconds / 60.0);
        var etaTime = currentTime.AddSeconds(totalSeconds).ToUniversalTime();

        return new PredictEtaResponseDto
        {
            EtaMinutes = etaMinutes,
            EtaDatetime = etaTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Confidence = 0.7,
            Factors = new Dictionary<string, object>
            {
                { "fallback", true },
                { "method", "rule_based_eta" },
                { "base_travel_mins", baseSeconds / 60.0 },
                { "dispatch_pickup_mins", dispatchPickupSeconds / 60.0 },
                { "traffic_multiplier", trafficMultiplier },
                { "weather_multiplier", weatherMultiplier },
                { "velocity_factor", velocityFactor }
            }
        };
    }
    #endregion
}
