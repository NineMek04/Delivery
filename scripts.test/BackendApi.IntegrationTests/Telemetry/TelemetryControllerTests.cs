using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;
using BackendApi.Features.FleetTracking.Telemetry;
using BackendApi.Features.FleetTracking.Models;
using BackendApi.Core.Models;

namespace BackendApi.IntegrationTests
{
    [Collection("SharedTestDatabase")]
    public class TelemetryControllerTests : IAsyncLifetime
    {
        private HttpClient _client = default!;
        private readonly DeliveryWebApplicationFactory _factory;
        private string _accessToken = string.Empty;
        private string _riderEmail = string.Empty;

        public TelemetryControllerTests(DeliveryWebApplicationFactory factory)
        {
            _factory = factory;
        }

        public async Task InitializeAsync()
        {
            _client = _factory.CreateClient();

            // Create a dedicated rider account for telemetry testing
            _riderEmail = $"rider_telemetry_{Guid.NewGuid():N}@test.com";
            
            var registerPayload = new
            {
                Email = _riderEmail,
                Password = "RiderPass123!",
                FullName = "Telemetry Rider",
                Role = "Rider"
            };

            var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerPayload);
            Assert.True(registerResponse.IsSuccessStatusCode);

            // Log in to retrieve the access token
            var loginPayload = new
            {
                Email = _riderEmail,
                Password = "RiderPass123!"
            };

            var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginPayload);
            Assert.True(loginResponse.IsSuccessStatusCode);

            var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponseWrapper<AuthData>>();
            Assert.NotNull(loginBody?.Value);
            _accessToken = loginBody.Value.AccessToken;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        // DTOs matching AuthFlowTests wrapper to deserialize login response
        private record ApiResponseWrapper<T>(bool Success, T? Value, string? Message, string? ErrorDetail);
        private record AuthData(string AccessToken, string RefreshToken, DateTime ExpiresAt);

        [Fact]
        public async Task PostGpsCoordinate_WithoutAuth_Returns401Unauthorized()
        {
            // Arrange
            var payload = new GpsPointRequest
            {
                Latitude = 13.7563,
                Longitude = 100.5018,
                Accuracy = 10.0
            };

            // Act - No Bearer token set
            var response = await _client.PostAsJsonAsync("/api/telemetry/gps", payload);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task PostGpsCoordinate_WithAuth_Returns200OK_And_IncludesRecommendedPingHeader()
        {
            // Arrange
            var payload = new GpsPointRequest
            {
                Latitude = 13.7563,
                Longitude = 100.5018,
                Accuracy = 5.0
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/telemetry/gps")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            // Act
            var response = await _client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Verify X-Recommended-Ping header is present
            Assert.True(response.Headers.Contains("X-Recommended-Ping"));
            var pingHeader = response.Headers.GetValues("X-Recommended-Ping").FirstOrDefault();
            Assert.False(string.IsNullOrWhiteSpace(pingHeader));
            
            // Should be a number, e.g. "3" for normal load
            Assert.True(int.TryParse(pingHeader, out int recommendedPing));
            Assert.True(recommendedPing >= 3);
        }

        [Fact]
        public async Task PostGpsCoordinate_WhenRateLimited_Returns429TooManyRequests()
        {
            // Arrange
            var payload = new GpsPointRequest
            {
                Latitude = 13.7563,
                Longitude = 100.5018,
                Accuracy = 5.0
            };

            // Helper to make an authenticated request
            Func<Task<HttpResponseMessage>> sendRequest = async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/telemetry/gps")
                {
                    Content = JsonContent.Create(payload)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                return await _client.SendAsync(request);
            };

            // Act
            // 1st request -> should pass (200 OK)
            var response1 = await sendRequest();
            Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

            // 2nd request immediately after -> should be rate limited (429 TooManyRequests)
            var response2 = await sendRequest();

            // Assert
            Assert.Equal(HttpStatusCode.TooManyRequests, response2.StatusCode);
            Assert.True(response2.Headers.Contains("X-Recommended-Ping"));
            
            var body = await response2.Content.ReadFromJsonAsync<ApiResponse<string>>();
            Assert.NotNull(body);
            Assert.Contains("throttled", body.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetMobileConfig_Anonymous_Returns200OK_WithMobileConfigResponse()
        {
            // Act
            var response = await _client.GetAsync("/api/telemetry/config/mobile");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<MobileConfigResponse>>();
            Assert.NotNull(body?.Value);
            Assert.True(body.Success);
            Assert.True(body.Value.IntervalSeconds >= 3);
            Assert.True(body.Value.ServerTime > DateTime.UtcNow.AddMinutes(-5));
        }

        [Fact]
        public async Task PostGpsBatch_WithoutAuth_Returns401Unauthorized()
        {
            // Arrange
            var payload = new List<GpsBatchPointRequest>
            {
                new GpsBatchPointRequest { Latitude = 13.7563, Longitude = 100.5018, Accuracy = 10.0, Timestamp = DateTime.UtcNow.AddSeconds(-10) },
                new GpsBatchPointRequest { Latitude = 13.7564, Longitude = 100.5019, Accuracy = 10.0, Timestamp = DateTime.UtcNow }
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/telemetry/gps/batch", payload);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task PostGpsBatch_WithAuth_Returns200OK_And_IncludesRecommendedPingHeader()
        {
            // Arrange
            var payload = new List<GpsBatchPointRequest>
            {
                new GpsBatchPointRequest { Latitude = 13.7563, Longitude = 100.5018, Accuracy = 5.0, Timestamp = DateTime.UtcNow.AddSeconds(-10) },
                new GpsBatchPointRequest { Latitude = 13.7564, Longitude = 100.5019, Accuracy = 5.0, Timestamp = DateTime.UtcNow }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/telemetry/gps/batch")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            // Act
            var response = await _client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Verify X-Recommended-Ping header is present
            Assert.True(response.Headers.Contains("X-Recommended-Ping"));
            var pingHeader = response.Headers.GetValues("X-Recommended-Ping").FirstOrDefault();
            Assert.False(string.IsNullOrWhiteSpace(pingHeader));
            Assert.True(int.TryParse(pingHeader, out int recommendedPing));
            Assert.True(recommendedPing >= 3);
        }

        [Fact]
        public async Task PostGpsBatch_WhenRateLimited_Returns429TooManyRequests()
        {
            // Arrange
            var payload = new List<GpsBatchPointRequest>
            {
                new GpsBatchPointRequest { Latitude = 13.7563, Longitude = 100.5018, Accuracy = 5.0, Timestamp = DateTime.UtcNow.AddSeconds(-10) },
                new GpsBatchPointRequest { Latitude = 13.7564, Longitude = 100.5019, Accuracy = 5.0, Timestamp = DateTime.UtcNow }
            };

            // Helper to make an authenticated batch request
            Func<Task<HttpResponseMessage>> sendRequest = async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/telemetry/gps/batch")
                {
                    Content = JsonContent.Create(payload)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                return await _client.SendAsync(request);
            };

            // Act
            // 1st request -> should pass (200 OK)
            var response1 = await sendRequest();
            Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

            // 2nd request immediately after -> should be rate limited (429 TooManyRequests)
            var response2 = await sendRequest();

            // Assert
            Assert.Equal(HttpStatusCode.TooManyRequests, response2.StatusCode);
            Assert.True(response2.Headers.Contains("X-Recommended-Ping"));
            
            var body = await response2.Content.ReadFromJsonAsync<ApiResponse<string>>();
            Assert.NotNull(body);
            Assert.Contains("throttled", body.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task PostGpsCoordinate_WithCustomerRole_Returns403Forbidden()
        {
            // Arrange: register and login a Customer
            var customerEmail = $"customer_telemetry_{Guid.NewGuid():N}@test.com";
            var registerPayload = new
            {
                Email = customerEmail,
                Password = "CustomerPass123!",
                FullName = "Telemetry Customer",
                Role = "Customer"
            };

            var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerPayload);
            Assert.True(registerResponse.IsSuccessStatusCode);

            var loginPayload = new
            {
                Email = customerEmail,
                Password = "CustomerPass123!"
            };

            var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginPayload);
            Assert.True(loginResponse.IsSuccessStatusCode);

            var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponseWrapper<AuthData>>();
            Assert.NotNull(loginBody?.Value);
            var customerToken = loginBody.Value.AccessToken;

            var payload = new GpsPointRequest
            {
                Latitude = 13.7563,
                Longitude = 100.5018,
                Accuracy = 5.0
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/telemetry/gps")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

            // Act
            var response = await _client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task PostGpsBatch_ExceedingLimit_Returns400BadRequest()
        {
            // Arrange: create batch of 101 items
            var payload = new List<GpsBatchPointRequest>();
            for (int i = 0; i < 101; i++)
            {
                payload.Add(new GpsBatchPointRequest
                {
                    Latitude = 13.7563,
                    Longitude = 100.5018,
                    Accuracy = 5.0,
                    Timestamp = DateTime.UtcNow.AddSeconds(-i)
                });
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/telemetry/gps/batch")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            // Act
            var response = await _client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
