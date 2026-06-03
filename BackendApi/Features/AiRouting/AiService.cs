using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Models.DTOs;

namespace BackendApi.Services.Ai;

public class AiService : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiService> _logger;

    public AiService(HttpClient httpClient, ILogger<AiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<DispatchRankResponseDto?> RankDispatchCandidatesAsync(DispatchRankRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/v1/dispatch/rank", request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to rank candidates. Status: {Status}, Body: {Body}", response.StatusCode, errorBody);
                return GenerateFallbackResponse(request);
            }

            var result = await response.Content.ReadFromJsonAsync<DispatchRankResponseDto>(cancellationToken: cancellationToken);
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
            var response = await _httpClient.PostAsJsonAsync("/api/optimize-route", request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to optimize route. Status: {Status}, Body: {Body}", response.StatusCode, errorBody);
                return GenerateFallbackRoutingResponse(request);
            }

            var result = await response.Content.ReadFromJsonAsync<RoutingResponseDto>(cancellationToken: cancellationToken);
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
            var response = await _httpClient.PostAsJsonAsync("/api/v1/predict-eta", request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to predict ETA. Status: {Status}, Body: {Body}", response.StatusCode, errorBody);
                return GenerateFallbackEtaResponse(request);
            }

            var result = await response.Content.ReadFromJsonAsync<PredictEtaResponseDto>(cancellationToken: cancellationToken);
            return result ?? GenerateFallbackEtaResponse(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while calling AI Engine for ETA prediction. Falling back to rule-based speed/traffic ETA estimation.");
            return GenerateFallbackEtaResponse(request);
        }
    }

    #region Fallback Mechanisms
    private double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371; // Earth's radius in km
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private double ToRadians(double val) => (Math.PI / 180) * val;

    private DispatchRankResponseDto GenerateFallbackResponse(DispatchRankRequestDto request)
    {
        var pickupLat = request.Order.Pickup.Count > 0 ? request.Order.Pickup[0] : 0;
        var pickupLng = request.Order.Pickup.Count > 1 ? request.Order.Pickup[1] : 0;

        var ranked = new List<RankedCandidateDto>();
        foreach (var c in request.Candidates)
        {
            var distance = CalculateHaversineDistance(c.Lat, c.Lng, pickupLat, pickupLng);
            var score = Math.Max(0.0, 100.0 - distance * 5.0);
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
            RankedCandidates = ranked.OrderByDescending(r => r.Score).ToList()
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
        var durationMinutes = request.RouteDurationSeconds > 0 
            ? request.RouteDurationSeconds / 60.0 
            : 15.0;

        if (request.TrafficLevel?.Equals("high", StringComparison.OrdinalIgnoreCase) == true)
        {
            durationMinutes *= 1.5;
        }
        else if (request.TrafficLevel?.Equals("medium", StringComparison.OrdinalIgnoreCase) == true)
        {
            durationMinutes *= 1.25;
        }

        if (request.WeatherCondition?.Equals("rainy", StringComparison.OrdinalIgnoreCase) == true)
        {
            durationMinutes *= 1.3;
        }

        var etaMinutes = Math.Ceiling(durationMinutes);
        return new PredictEtaResponseDto
        {
            EtaMinutes = etaMinutes,
            EtaDatetime = DateTime.UtcNow.AddMinutes(etaMinutes).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Confidence = 0.7,
            Factors = new Dictionary<string, object>
            {
                { "fallback", true },
                { "method", "haversine_rule" }
            }
        };
    }
    #endregion
}
