using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using StackExchange.Redis;
using Xunit;
using BackendApi.Services.Ai;

namespace BackendApi.UnitTests.AiRouting
{
    /// <summary>
    /// Unit tests for OsrmRoutingService covering:
    ///  - Bug #2: JSON parse fallback on malformed /trip response
    ///  - Bug #4: Invariant-culture cache key generation
    ///  - General resilience: Redis cache hit/miss, sequential fallback on HTTP failure
    /// </summary>
    public class OsrmRoutingServiceTests
    {
        // ── Helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Builds an HttpClient whose inner handler returns the provided response for every call.
        /// </summary>
        private static HttpClient BuildHttpClient(HttpResponseMessage response)
        {
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(response);

            return new HttpClient(handlerMock.Object);
        }

        private static OsrmRoutingService BuildService(
            HttpClient httpClient,
            IDatabase redisDb,
            IConnectionMultiplexer? redisMux = null)
        {
            var mux = redisMux ?? BuildRedisMux(redisDb);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "Routing:LocalOsrmUrl", "http://localhost:5001" }
                })
                .Build();
            var logger = new Mock<ILogger<OsrmRoutingService>>().Object;
            return new OsrmRoutingService(httpClient, mux, config, logger);
        }

        private static IConnectionMultiplexer BuildRedisMux(IDatabase db)
        {
            var mux = new Mock<IConnectionMultiplexer>();
            mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db);
            return mux.Object;
        }

        private static Mock<IDatabase> EmptyCacheDb()
        {
            var db = new Mock<IDatabase>();
            // No cached value
            db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(RedisValue.Null);
            // Cache write silently succeeds
            db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(true);
            return db;
        }

        // ─────────────────────────────────────────────────────────────────
        // Bug #4 — Invariant-culture cache key
        // ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetRouteDetailsAsync_CacheKey_MustUseInvariantCultureDotDecimalSeparator()
        {
            // Arrange — coordinates whose string repr differs under locale settings
            double lat1 = 13.12345, lng1 = 100.67891, lat2 = 13.98765, lng2 = 100.54321;

            // Expected key (always uses InvariantCulture / dot separator)
            var expectedKey = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "route:cache:{0:F5}:{1:F5}:{2:F5}:{3:F5}",
                lat1, lng1, lat2, lng2);

            string? capturedReadKey  = null;
            string? capturedWriteKey = null;

            var db = new Mock<IDatabase>();

            // Capture the key used in the cache-read call (no CommandFlags overload)
            db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
              .Callback<RedisKey, CommandFlags>((k, _) => capturedReadKey = k.ToString())
              .ReturnsAsync(RedisValue.Null);

            // Capture the key used in the cache-write call
            db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
              .Callback<RedisKey, RedisValue, TimeSpan?, When, CommandFlags>((k, _, _, _, _) => capturedWriteKey = k.ToString())
              .ReturnsAsync(true);

            var osrmJson = BuildOsrmRouteJson(100.0, 30.0, new[] { new[] { lng1, lat1 }, new[] { lng2, lat2 } });
            var svc = BuildService(BuildHttpClient(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(osrmJson)
            }), db.Object);

            // Act
            await svc.GetRouteDetailsAsync(lat1, lng1, lat2, lng2);

            // Assert — the key sent to Redis must match InvariantCulture format (dots, not commas)
            var actualKey = capturedReadKey ?? capturedWriteKey;
            Assert.NotNull(actualKey);
            Assert.Equal(expectedKey, actualKey);
            // Verify the coordinate segments use dots (.) not commas (,) as decimal separator
            // Split off the "route:cache:" prefix, then check remaining coordinate tokens
            var segments = actualKey!.Substring("route:cache:".Length).Split(':');
            Assert.Equal(4, segments.Length); // must have exactly 4 coordinate tokens
            foreach (var seg in segments)
            {
                // Each segment is a decimal number like "13.12345" — must have dot not comma
                Assert.Matches(@"^\-?\d+\.\d+$", seg);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Bug #2 — GetOptimizedTripSequenceAsync JSON parse fallback
        // ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetOptimizedTripSequenceAsync_WhenJsonIsMalformed_ShouldReturnSequentialFallback()
        {
            // Arrange — OSRM returns HTTP 200 but invalid JSON body
            var badJson = "{ this is definitely not valid json }}}";
            var db = EmptyCacheDb();
            var svc = BuildService(BuildHttpClient(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(badJson)
            }), db.Object);

            var points = new List<(double Lat, double Lng)>
            {
                (13.7, 100.5),
                (13.8, 100.6),
                (13.9, 100.7),
            };

            // Act
            var result = await svc.GetOptimizedTripSequenceAsync(points);

            // Assert — sequential fallback [0, 1, 2]
            Assert.NotNull(result);
            Assert.Equal(new[] { 0, 1, 2 }, result);
        }

        [Fact]
        public async Task GetOptimizedTripSequenceAsync_WhenResponseMissingWaypointsProperty_ShouldReturnSequentialFallback()
        {
            // Arrange — OSRM returns HTTP 200 with valid JSON but no "waypoints" key
            var noWaypointsJson = JsonSerializer.Serialize(new { code = "Ok", trips = new object[] { } });
            var db = EmptyCacheDb();
            var svc = BuildService(BuildHttpClient(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(noWaypointsJson)
            }), db.Object);

            var points = new List<(double Lat, double Lng)>
            {
                (13.7, 100.5),
                (13.8, 100.6),
                (14.0, 100.8),
            };

            // Act
            var result = await svc.GetOptimizedTripSequenceAsync(points);

            // Assert
            Assert.Equal(new[] { 0, 1, 2 }, result);
        }

        [Fact]
        public async Task GetOptimizedTripSequenceAsync_WhenWaypointIndexPropertyMissing_ShouldReturnSequentialFallback()
        {
            // Arrange — waypoints present but each waypoint lacks "waypoint_index"
            var json = JsonSerializer.Serialize(new
            {
                code = "Ok",
                waypoints = new[]
                {
                    new { name = "Stop A", no_waypoint_index = 0 },  // wrong property name
                    new { name = "Stop B", no_waypoint_index = 1 }
                }
            });
            var db = EmptyCacheDb();
            var svc = BuildService(BuildHttpClient(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            }), db.Object);

            var points = new List<(double Lat, double Lng)>
            {
                (13.7, 100.5), (13.8, 100.6), (13.9, 100.7)
            };

            // Act
            var result = await svc.GetOptimizedTripSequenceAsync(points);

            // Assert
            Assert.Equal(new[] { 0, 1, 2 }, result);
        }

        [Fact]
        public async Task GetOptimizedTripSequenceAsync_WhenHttpNonSuccess_ShouldReturnSequentialFallback()
        {
            // Arrange — OSRM returns 500
            var db = EmptyCacheDb();
            var svc = BuildService(BuildHttpClient(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("")
            }), db.Object);

            var points = new List<(double Lat, double Lng)>
            {
                (13.7, 100.5), (13.8, 100.6), (13.9, 100.7), (14.0, 100.9)
            };

            // Act
            var result = await svc.GetOptimizedTripSequenceAsync(points);

            // Assert — 4-point sequential fallback
            Assert.Equal(new[] { 0, 1, 2, 3 }, result);
        }

        [Fact]
        public async Task GetOptimizedTripSequenceAsync_WhenValidResponse_ShouldReturnOptimizedOrder()
        {
            // Arrange — OSRM returns a valid trip with re-ordered waypoints: 0 → 2 → 1
            var json = JsonSerializer.Serialize(new
            {
                code = "Ok",
                waypoints = new[]
                {
                    new { waypoint_index = 0 },
                    new { waypoint_index = 2 },
                    new { waypoint_index = 1 }
                }
            });
            var db = EmptyCacheDb();
            var svc = BuildService(BuildHttpClient(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            }), db.Object);

            var points = new List<(double Lat, double Lng)>
            {
                (13.7, 100.5), (13.8, 100.6), (13.9, 100.7)
            };

            // Act
            var result = await svc.GetOptimizedTripSequenceAsync(points);

            // Assert — OSRM ordering preserved
            Assert.Equal(new[] { 0, 2, 1 }, result);
        }

        [Fact]
        public async Task GetOptimizedTripSequenceAsync_WithTwoOrFewerPoints_ShouldReturnSequentialWithoutCallingOsrm()
        {
            // Arrange — 2 points: short-circuit, no HTTP call
            var dbMock = EmptyCacheDb();
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                });
            var httpClient = new HttpClient(handlerMock.Object);

            var svc = BuildService(httpClient, dbMock.Object);
            var points = new List<(double Lat, double Lng)> { (13.7, 100.5), (13.8, 100.6) };

            // Act
            var result = await svc.GetOptimizedTripSequenceAsync(points);

            // Assert
            Assert.Equal(new[] { 0, 1 }, result);
            // No HTTP call should be made for ≤2 points
            handlerMock.Protected().Verify("SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
        }

        // ─────────────────────────────────────────────────────────────────
        // GetRouteDetailsAsync — Redis cache hit path
        // ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetRouteDetailsAsync_WhenCacheHit_ShouldReturnCachedDataWithoutCallingOsrm()
        {
            // Arrange
            var cachedPayload = JsonSerializer.Serialize(new
            {
                polyline = "abc123",
                distance = 1500.0,
                duration = 180.0
            });

            var db = new Mock<IDatabase>();
            db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(new RedisValue(cachedPayload));

            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));
            var httpClient = new HttpClient(handlerMock.Object);

            var svc = BuildService(httpClient, db.Object);

            // Act
            var (polyline, distance, duration) = await svc.GetRouteDetailsAsync(13.7, 100.5, 13.8, 100.6);

            // Assert
            Assert.Equal("abc123", polyline);
            Assert.Equal(1500.0, distance);
            Assert.Equal(180.0, duration);

            // OSRM should never be called when cache is warm
            handlerMock.Protected().Verify("SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
        }

        // ─────────────────────────────────────────────────────────────────
        // GetRouteDetailsAsync — Redis write failure does not crash route call
        // ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetRouteDetailsAsync_WhenRedisCacheWriteFails_ShouldStillReturnRoute()
        {
            // Arrange
            var db = new Mock<IDatabase>();
            db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(RedisValue.Null);
            db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
              .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis write failed"));

            var lat1 = 13.7; var lng1 = 100.5; var lat2 = 13.8; var lng2 = 100.6;
            var osrmJson = BuildOsrmRouteJson(2000.0, 240.0, new[]
            {
                new[] { lng1, lat1 }, new[] { lng2, lat2 }
            });
            var svc = BuildService(BuildHttpClient(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(osrmJson)
            }), db.Object);

            // Act — must not throw even though Redis write fails
            var (polyline, distance, duration) = await svc.GetRouteDetailsAsync(lat1, lng1, lat2, lng2);

            // Assert
            Assert.Equal(2000.0, distance);
            Assert.Equal(240.0, duration);
        }

        // ─────────────────────────────────────────────────────────────────
        // SnapToRoadAsync — graceful fallback on OSRM failure
        // ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task SnapToRoadAsync_WhenOsrmFails_ShouldReturnOriginalCoordinate()
        {
            // Arrange — every HTTP call throws (simulates total OSRM outage)
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("OSRM unreachable"));

            var db = EmptyCacheDb();
            var svc = BuildService(new HttpClient(handlerMock.Object), db.Object);

            double inputLat = 13.7563, inputLng = 100.5018;

            // Act
            var (snappedLat, snappedLng) = await svc.SnapToRoadAsync(inputLat, inputLng);

            // Assert — original coordinate is returned as fallback
            Assert.Equal(inputLat, snappedLat);
            Assert.Equal(inputLng, snappedLng);
        }

        // ─────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────

        private static string BuildOsrmRouteJson(double distance, double duration, double[][] coords)
        {
            var coordElements = System.Text.Json.Nodes.JsonNode.Parse(
                JsonSerializer.Serialize(coords))!;

            return JsonSerializer.Serialize(new
            {
                code = "Ok",
                routes = new[]
                {
                    new
                    {
                        distance,
                        duration,
                        geometry = new
                        {
                            type = "LineString",
                            coordinates = coords
                        }
                    }
                }
            });
        }
    }
}
