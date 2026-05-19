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
            var cacheKey = $"route:cache:{startLat:F5}:{startLng:F5}:{endLat:F5}:{endLng:F5}";

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
    }
}
