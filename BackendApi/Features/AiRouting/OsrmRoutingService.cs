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
            var timeoutMs = int.TryParse(config?["Routing:OsrmTimeoutMs"], out var configuredTimeoutMs)
                ? configuredTimeoutMs
                : 5000;
            _httpClient.Timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 1000, 15000));
            _redis = redis;
            _logger = logger;
            _localOsrmUrl = config?["Routing:LocalOsrmUrl"] ?? "http://localhost:5001";
        }

        public async Task<(string Polyline, double DistanceMeters, double DurationSeconds, List<double[]> Coordinates)> GetRouteDetailsAsync(
            double startLat, double startLng, double endLat, double endLng)
        {
            using var timer = OperationalMetrics.OsrmRequestDuration.WithLabels("route").NewTimer();
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
                    OperationalMetrics.RoutingRequestsTotal.WithLabels("osrm").Inc();
                    using var doc = JsonDocument.Parse(cached.ToString());
                    var root = doc.RootElement;
                    var cachedCoordinates = new List<double[]>();
                    if (root.TryGetProperty("coordinates", out var coordinatesElement) &&
                        coordinatesElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var point in coordinatesElement.EnumerateArray())
                        {
                            if (point.ValueKind == JsonValueKind.Array && point.GetArrayLength() >= 2)
                            {
                                cachedCoordinates.Add(new[]
                                {
                                    point[0].GetDouble(),
                                    point[1].GetDouble()
                                });
                            }
                        }
                    }

                    if (cachedCoordinates.Count >= 2)
                    {
                        return (
                            root.GetProperty("polyline").GetString() ?? string.Empty,
                            root.GetProperty("distance").GetDouble(),
                            root.GetProperty("duration").GetDouble(),
                            cachedCoordinates
                        );
                    }

                    _logger.LogInformation(
                        "Route cache entry has no coordinates; refreshing from Local OSRM: {Key}",
                        cacheKey);
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

            try
            {
                return await resilientPolicy.ExecuteAsync(async () =>
                {
                    // ตรวจสอบพิกัดเริ่มต้นและสิ้นสุด
                    var lat1 = startLat.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var lng1 = startLng.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var lat2 = endLat.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var lng2 = endLng.ToString(System.Globalization.CultureInfo.InvariantCulture);

                    // [PDPA FIX] Local OSRM only — public fallback removed to prevent GPS data leakage.
                    // When local OSRM is unavailable the Polly circuit breaker opens and
                    // the outer catch returns a safe Haversine straight-line estimate.
                    var url = $"{_localOsrmUrl}/route/v1/driving/{lng1},{lat1};{lng2},{lat2}?overview=full&geometries=geojson";
                    
                    _logger.LogInformation("Calling Local OSRM: {Url}", url);
                    HttpResponseMessage response = await _httpClient.GetAsync(url);

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
                            var coordinates = new List<double[]>();
                            foreach (var point in coords.EnumerateArray())
                            {
                                // OSRM คืนค่าเป็น [lng, lat] เสมอ ให้สลับเป็น [lat, lng] เพื่อป้อนเข้า PolylineEncoder
                                var lng = point[0].GetDouble();
                                var lat = point[1].GetDouble();
                                coordinates.Add(new[] { lng, lat });
                                list.Add(new double[] { lat, lng });
                            }

                            // เข้ารหัสพิกัดด้วย Google Polyline (ประหยัดพื้นที่จัดเก็บ 99%)
                            var polyline = PolylineEncoder.Encode(list);

                            OperationalMetrics.RoutingRequestsTotal.WithLabels("osrm").Inc();

                            // บันทึกผลลัพธ์ลง Redis Cache (เก็บไว้ 24 ชั่วโมง)
                            try
                            {
                                var cacheData = new { polyline, distance, duration, coordinates };
                                await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(cacheData), TimeSpan.FromHours(24));
                                _logger.LogInformation("Route details successfully saved to Redis Cache: {Key}", cacheKey);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to write route cache to Redis.");
                            }

                            return (polyline, distance, duration, coordinates);
                        }
                    }

                    throw new HttpRequestException($"OSRM routing server returned unsuccessful status: {response.StatusCode}");
                });
            }
            catch (Exception ex)
            {
                // Local OSRM unavailable (circuit open, timeout, connection refused).
                // Fall back to Haversine straight-line — safe, local, no external calls.
                OperationalMetrics.RoutingRequestsTotal.WithLabels("haversine").Inc();
                _logger.LogWarning(ex, "Local OSRM unavailable for GetRouteDetailsAsync. Falling back to Haversine estimate.");
                return HaversineRouteFallback(startLat, startLng, endLat, endLng);
            }
        }

        /// <summary>
        /// [PDPA SAFE] Haversine straight-line fallback — used when local OSRM is unavailable.
        /// Returns an empty polyline, straight-line distance (meters), and estimated duration
        /// based on average urban speed (25 km/h). No GPS data leaves the local network.
        /// </summary>
        private static (string Polyline, double DistanceMeters, double DurationSeconds, List<double[]> Coordinates) HaversineRouteFallback(
            double startLat, double startLng, double endLat, double endLng)
        {
            const double R = 6_371_000; // Earth radius in metres
            var dLat = (endLat - startLat) * Math.PI / 180.0;
            var dLon = (endLng - startLng) * Math.PI / 180.0;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                  + Math.Cos(startLat * Math.PI / 180.0) * Math.Cos(endLat * Math.PI / 180.0)
                  * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var distanceMeters = R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            // Assume 25 km/h urban average; add 20% buffer
            var durationSeconds = (distanceMeters / 1000.0 / 25.0) * 3600.0 * 1.2;

            return (string.Empty, distanceMeters, durationSeconds, new List<double[]>());
        }

    }
}
