using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BackendApi.Core.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Polly;
using Polly.CircuitBreaker;
using BackendApi.Services.Telemetry;
using Prometheus;

namespace BackendApi.Services.Ai
{
    public partial class OsrmRoutingService
    {
        /// <summary>
        /// ดึงพิกัดจุดบนถนนที่ใกล้ที่สุดเพื่อป้องกันพิกัดไรเดอร์วาร์ป (Snap-to-Road)
        /// </summary>
        public async Task<(double Lat, double Lng)> SnapToRoadAsync(double lat, double lng)
        {
            var roundedLat = Math.Round(lat, 4);
            var roundedLng = Math.Round(lng, 4);
            var cacheKey = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "route:snap:cache:{0:F4}:{1:F4}",
                roundedLat, roundedLng);

            var db = _redis.GetDatabase();

            try
            {
                var cached = await db.StringGetAsync(cacheKey);
                if (cached.HasValue)
                {
                    _logger.LogInformation("Snapped coordinates retrieved from Redis Cache: {Key}", cacheKey);
                    using var doc = JsonDocument.Parse(cached.ToString());
                    var root = doc.RootElement;
                    var cachedLat = root.GetProperty("lat").GetDouble();
                    var cachedLng = root.GetProperty("lng").GetDouble();
                    return (cachedLat, cachedLng);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read snap cache from Redis.");
            }

            using var timer = OperationalMetrics.OsrmRequestDuration.WithLabels("nearest").NewTimer();
            var latStr = lat.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var lngStr = lng.ToString(System.Globalization.CultureInfo.InvariantCulture);

            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(1, retryAttempt => TimeSpan.FromMilliseconds(50));

            var resilientPolicy = Policy.WrapAsync(retryPolicy, _circuitBreakerPolicy);

            try
            {
                return await resilientPolicy.ExecuteAsync(async () =>
                {
                    var url = $"{_localOsrmUrl}/nearest/v1/driving/{lngStr},{latStr}?number=1";
                    // [PDPA FIX] Local OSRM only — no public fallback to prevent GPS data leakage
                    HttpResponseMessage response = await _httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using var document = JsonDocument.Parse(json);
                        var root = document.RootElement;
                        if (root.TryGetProperty("waypoints", out var waypoints) && waypoints.GetArrayLength() > 0)
                        {
                            var waypoint = waypoints[0];
                            var location = waypoint.GetProperty("location");
                            var snappedLng = location[0].GetDouble();
                            var snappedLat = location[1].GetDouble();

                            // Save to Redis Snap Cache
                            try
                            {
                                var cacheData = new { lat = snappedLat, lng = snappedLng };
                                await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(cacheData), TimeSpan.FromHours(24));
                                _logger.LogInformation("Snapped coordinates successfully saved to Redis Cache: {Key}", cacheKey);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to write snap cache to Redis.");
                            }

                            return (snappedLat, snappedLng);
                        }
                    }

                    throw new HttpRequestException($"OSRM nearest returned unsuccessful status: {response.StatusCode}");
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to snap coordinate ({Lat}, {Lng}) to road. Using raw coordinate as fallback.", lat, lng);
                return (lat, lng); // fallback to original point
            }
        }

        /// <summary>
        /// ใช้ OSRM /trip API เพื่อแก้ปัญหา TSP (Traveling Salesperson Problem) สำหรับจัดลำดับจุดส่งหลายจุด
        /// รับพิกัดเริ่มต้น (Pickup) และจุดส่ง (Dropoffs) เรียงลำดับที่เหมาะสมที่สุด
        /// </summary>
        public async Task<List<int>> GetOptimizedTripSequenceAsync(List<(double Lat, double Lng)> points)
        {
            using var timer = OperationalMetrics.OsrmRequestDuration.WithLabels("trip").NewTimer();
            if (points.Count <= 2)
            {
                var seq = new List<int>();
                for (int i = 0; i < points.Count; i++) seq.Add(i);
                return seq;
            }

            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(1, retryAttempt => TimeSpan.FromMilliseconds(100));

            var resilientPolicy = Policy.WrapAsync(retryPolicy, _circuitBreakerPolicy);

            try
            {
                return await resilientPolicy.ExecuteAsync(async () =>
            {
                var coordinatesStr = string.Join(";", points.Select(p => 
                    $"{p.Lng.ToString(System.Globalization.CultureInfo.InvariantCulture)},{p.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));

                // source=first means start at the pickup point, roundtrip=false means we don't return to pickup
                var url = $"{_localOsrmUrl}/trip/v1/driving/{coordinatesStr}?source=first&roundtrip=false";
                
                // [PDPA FIX] Local OSRM only — no public fallback to prevent GPS data leakage
                HttpResponseMessage response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    // [RESILIENCE FIX] Wrap JSON parsing in try-catch so that a malformed OSRM
                    // response (HTTP 200 but invalid/empty JSON) gracefully returns sequential fallback
                    // instead of throwing JsonException / KeyNotFoundException up to BatchEvaluator.
                    List<int>? parsed = null;
                    try
                    {
                        using var document = JsonDocument.Parse(json);
                        var root = document.RootElement;

                        if (root.TryGetProperty("waypoints", out var waypoints))
                        {
                            var originalIndexes = new List<int>();
                            foreach (var waypoint in waypoints.EnumerateArray())
                            {
                                var originalIndex = waypoint.GetProperty("waypoint_index").GetInt32();
                                originalIndexes.Add(originalIndex);
                            }
                            parsed = originalIndexes;
                        }
                    }
                    catch (Exception parseEx)
                    {
                        _logger.LogWarning(parseEx, "OSRM trip response JSON parse failed. Falling back to sequential order.");
                    }

                    if (parsed != null)
                        return parsed;
                }

                _logger.LogWarning("OSRM trip returned unsuccessful status: {Status}. Falling back to sequential.", response.StatusCode);
                var fallback = new List<int>();
                for (int i = 0; i < points.Count; i++) fallback.Add(i);
                return fallback;
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Local OSRM unavailable for trip sequence. Falling back to sequential order.");
                var fallback = new List<int>();
                for (int i = 0; i < points.Count; i++) fallback.Add(i);
                return fallback;
            }
        }
        }
}
