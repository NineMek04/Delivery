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

namespace BackendApi.Services.Ai
{
    public class OsrmRoutingService
    {
        private readonly HttpClient _httpClient;
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<OsrmRoutingService> _logger;
        private readonly string _localOsrmUrl;
        
        // Static policy to share Circuit Breaker state across requests
        private static readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromSeconds(15)
            );

        public OsrmRoutingService(
            HttpClient httpClient,
            IConnectionMultiplexer redis,
            IConfiguration config,
            ILogger<OsrmRoutingService> logger)
        {
            _httpClient = httpClient;
            // ตั้งค่า Strict Timeout 1.5 วินาที
            _httpClient.Timeout = TimeSpan.FromMilliseconds(1500);
            _redis = redis;
            _logger = logger;
            _localOsrmUrl = config["Routing:LocalOsrmUrl"] ?? "http://localhost:5001";
        }

        public async Task<(string Polyline, double DistanceMeters, double DurationSeconds)> GetRouteDetailsAsync(
            double startLat, double startLng, double endLat, double endLng)
        {
            var db = _redis.GetDatabase();
            // [CULTURE FIX] Use InvariantCulture to prevent locale-specific decimal separators
            // (e.g. German/French OS uses comma instead of dot) from corrupting Redis cache keys.
            var cacheKey = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "route:cache:{0:F5}:{1:F5}:{2:F5}:{3:F5}",
                startLat, startLng, endLat, endLng);

            // 1. ค้นหา Cache จาก Redis
            try
            {
                var cached = await db.StringGetAsync(cacheKey);
                if (cached.HasValue)
                {
                    _logger.LogInformation("Route details retrieved from Redis Cache: {Key}", cacheKey);
                    using var doc = JsonDocument.Parse(cached.ToString());
                    var root = doc.RootElement;
                    return (
                        root.GetProperty("polyline").GetString() ?? string.Empty,
                        root.GetProperty("distance").GetDouble(),
                        root.GetProperty("duration").GetDouble()
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read route cache from Redis.");
            }

            // 2. ตั้งค่า Polly Retry Policy (Retry 2 ครั้ง โดยเว้นระยะเพิ่มขึ้น)
            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromMilliseconds(100 * retryAttempt));

            // รวม Retry และ Circuit Breaker เข้าด้วยกัน
            var resilientPolicy = Policy.WrapAsync(retryPolicy, _circuitBreakerPolicy);

            return await resilientPolicy.ExecuteAsync(async () =>
            {
                // ตรวจสอบพิกัดเริ่มต้นและสิ้นสุด
                var lat1 = startLat.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var lng1 = startLng.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var lat2 = endLat.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var lng2 = endLng.ToString(System.Globalization.CultureInfo.InvariantCulture);

                // พยายามเรียก Local OSRM ก่อน
                var url = $"{_localOsrmUrl}/route/v1/driving/{lng1},{lat1};{lng2},{lat2}?overview=full&geometries=geojson";
                
                HttpResponseMessage response;
                try
                {
                    _logger.LogInformation("Calling Local OSRM: {Url}", url);
                    response = await _httpClient.GetAsync(url);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Local OSRM request failed. Trying Public OSRM as fallback for demo.");
                    var publicUrl = $"http://router.project-osrm.org/route/v1/driving/{lng1},{lat1};{lng2},{lat2}?overview=full&geometries=geojson";
                    response = await _httpClient.GetAsync(publicUrl);
                }

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var document = JsonDocument.Parse(json);
                    var root = document.RootElement;
                    if (root.TryGetProperty("routes", out var routes) && routes.GetArrayLength() > 0)
                    {
                        var firstRoute = routes[0];
                        var distance = firstRoute.GetProperty("distance").GetDouble();
                        var duration = firstRoute.GetProperty("duration").GetDouble();
                        
                        var geometry = firstRoute.GetProperty("geometry");
                        var coords = geometry.GetProperty("coordinates");
                        var list = new List<double[]>();
                        foreach (var point in coords.EnumerateArray())
                        {
                            // OSRM คืนค่าเป็น [lng, lat] เสมอ ให้สลับเป็น [lat, lng] เพื่อป้อนเข้า PolylineEncoder
                            var lng = point[0].GetDouble();
                            var lat = point[1].GetDouble();
                            list.Add(new double[] { lat, lng });
                        }

                        // เข้ารหัสพิกัดด้วย Google Polyline (ประหยัดพื้นที่จัดเก็บ 99%)
                        var polyline = PolylineEncoder.Encode(list);

                        // บันทึกผลลัพธ์ลง Redis Cache (เก็บไว้ 24 ชั่วโมง)
                        try
                        {
                            var cacheData = new { polyline, distance, duration };
                            await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(cacheData), TimeSpan.FromHours(24));
                            _logger.LogInformation("Route details successfully saved to Redis Cache: {Key}", cacheKey);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to write route cache to Redis.");
                        }

                        return (polyline, distance, duration);
                    }
                }

                throw new HttpRequestException($"OSRM routing server returned unsuccessful status: {response.StatusCode}");
            });
        }

        /// <summary>
        /// ดึงพิกัดจุดบนถนนที่ใกล้ที่สุดเพื่อป้องกันพิกัดไรเดอร์วาร์ป (Snap-to-Road)
        /// </summary>
        public async Task<(double Lat, double Lng)> SnapToRoadAsync(double lat, double lng)
        {
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
                    HttpResponseMessage response;
                    try
                    {
                        response = await _httpClient.GetAsync(url);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Local OSRM nearest query failed. Falling back to public OSRM nearest API.");
                        var publicUrl = $"http://router.project-osrm.org/nearest/v1/driving/{lngStr},{latStr}?number=1";
                        response = await _httpClient.GetAsync(publicUrl);
                    }

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

            return await resilientPolicy.ExecuteAsync(async () =>
            {
                var coordinatesStr = string.Join(";", points.Select(p => 
                    $"{p.Lng.ToString(System.Globalization.CultureInfo.InvariantCulture)},{p.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));

                // source=first means start at the pickup point, roundtrip=false means we don't return to pickup
                var url = $"{_localOsrmUrl}/trip/v1/driving/{coordinatesStr}?source=first&roundtrip=false";
                
                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.GetAsync(url);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Local OSRM trip request failed. Trying Public OSRM.");
                    var publicUrl = $"http://router.project-osrm.org/trip/v1/driving/{coordinatesStr}?source=first&roundtrip=false";
                    response = await _httpClient.GetAsync(publicUrl);
                }

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
    }
}
