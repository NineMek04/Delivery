using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Models.DTOs;
using BackendApi.Services.Ai;
using BackendApi.Infrastructure.Redis;

namespace BackendApi.Services.Dispatch;

/// <summary>
/// Candidate Ranker — สื่อสารกับ Optimization service เพื่อจัดอันดับ Rider ที่เหมาะสมที่สุด
/// Fallback: ถ้า service ล่ม ใช้ Haversine distance-based ranking
/// </summary>
public class DispatchCandidateRanker
{
    private readonly IAiService _aiService;
    private readonly RiderPresenceService _presenceService;
    private readonly ILogger<DispatchCandidateRanker> _logger;

    public DispatchCandidateRanker(
        IAiService aiService,
        RiderPresenceService presenceService,
        ILogger<DispatchCandidateRanker> logger)
    {
        _aiService = aiService;
        _presenceService = presenceService;
        _logger = logger;
    }

    /// <summary>
    /// ส่งรายชื่อ Candidates ไปให้ Optimization service เพื่อให้คะแนนและจัดอันดับ (Phase A weighted heuristic)
    /// </summary>
    public async Task<List<RankedCandidate>> RankCandidatesAsync(
        Order order, List<(string RiderId, double DistanceKm, double Lat, double Lng)> candidates, Dictionary<string, Rider> ridersDict)
    {
        try
        {
            // Limit to top 150 closest candidates to prevent ranking service overload (max 200 candidates limit)
            if (candidates.Count > 150)
            {
                candidates = candidates.OrderBy(c => c.DistanceKm).Take(150).ToList();
            }
                // ดึง rider speed จาก Redis 5-point Moving Average
                var candidateDtos = new List<DispatchCandidateDto>();
                foreach (var c in candidates)
                {
                    ridersDict.TryGetValue(c.RiderId, out var rider);
                    var riderSpeed = await _presenceService.GetRiderSpeedAsync(c.RiderId);
                    candidateDtos.Add(new DispatchCandidateDto
                    {
                        RiderId = c.RiderId,
                        Lat = c.Lat,
                        Lng = c.Lng,
                        SpeedKmh = riderSpeed > 0 ? riderSpeed : 20.0, // Default 20 km/h
                        CurrentTasks = new List<Dictionary<string, object>>()
                    });
                }

                var request = new DispatchRankRequestDto
                {
                    Context = new DispatchContextDto
                    {
                        Timestamp = DateTime.UtcNow.ToString("O"),
                        City = "Bangkok"
                    },
                    Order = new DispatchOrderDto
                    {
                        Id = order.Id,
                        Pickup = new List<double> { order.PickupLocation?.Y ?? 0, order.PickupLocation?.X ?? 0 },
                        Dropoff = new List<double> { order.DropoffLocation?.Y ?? 0, order.DropoffLocation?.X ?? 0 },
                        SlaLimitMinutes = order.SlaLimitMinutes
                    },
                    Candidates = candidateDtos
                };

            var aiResponse = await _aiService.RankDispatchCandidatesAsync(request);

            if (aiResponse is not null && aiResponse.RankedCandidates.Any())
            {
                var rankedList = new List<RankedCandidate>();
                foreach (var item in aiResponse.RankedCandidates)
                {
                    if (!string.IsNullOrEmpty(item.RiderId))
                    {
                        rankedList.Add(new RankedCandidate(item.RiderId, item.DistanceToPickupKm, item.Score, item.EtaMinutes));
                    }
                }
                return rankedList;
            }
            
            _logger.LogWarning("Optimization service returned null or empty. Falling back to distance-based ranking.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Fallback Rule-Based Dispatch] Optimization service is down or timed out. Falling back to straight-line Haversine / Redis distance-based nearest selection for Order {OrderId}.", order.Id);
        }

        return candidates.OrderBy(c => c.DistanceKm)
                         .Select(c => new RankedCandidate(c.RiderId, c.DistanceKm, c.DistanceKm * 5.0, (int)Math.Ceiling(c.DistanceKm * 2.0)))
                         .ToList();
    }
}

/// <summary>
/// ผลลัพธ์ Ranked Candidate ที่ได้จาก Optimization service หรือ Fallback
/// </summary>
public record RankedCandidate(string RiderId, double DistanceKm, double Score, int EtaMinutes);


