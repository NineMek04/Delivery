using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BackendApi.Core.DataHandlers;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BackendApi.Services.Notifications
{
    /// <summary>
    /// บริการส่งข้อความแจ้งเตือน Firebase Cloud Messaging (FCM)
    /// รองรับ Simulation Mode พ่น Structured Log เข้าตู้เก็บ Telemetry (Seq) เมื่อไม่ตั้งค่า Firebase
    /// </summary>
    public class FcmNotificationService : IFcmNotificationService
    {
        private readonly DBHandlerCore _db;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<FcmNotificationService> _logger;

        public FcmNotificationService(
            DBHandlerCore db,
            IConfiguration config,
            IHttpClientFactory httpClientFactory,
            ILogger<FcmNotificationService> logger)
        {
            _db = db;
            _config = config;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<int> SendNotificationToUserAsync(string userId, string title, string body, Dictionary<string, string>? data = null)
        {
            // ดึง FCM Tokens ทั้งหมดของผู้ใช้ผ่าน DBHandlerCore (ห้ามฉีด DbContext ตรงตามกฎเหล็ก)
            var tokens = await _db.GetQuery<FcmToken>()
                .Where(t => t.UserId == userId)
                .Select(t => t.Token)
                .ToListAsync();

            if (!tokens.Any())
            {
                _logger.LogWarning("No FCM tokens registered for User {UserId}. Notification skipped.", userId);
                return 0;
            }

            int successCount = 0;
            foreach (var token in tokens)
            {
                var success = await SendNotificationToTokenAsync(token, title, body, data);
                if (success) successCount++;
            }

            // แสดงผลจำลองโครงข่ายในตู้เก็บสถิติ Seq อย่างหรูหราด้วย Structured Logging
            _logger.LogInformation(
                "FCM Notification Summary: Sent to User {UserId}. Success: {SuccessCount}/{TotalCount}. Payload: {@NotificationPayload}",
                userId,
                successCount,
                tokens.Count,
                new
                {
                    Title = title,
                    Body = body,
                    Data = data ?? new Dictionary<string, string>(),
                    Timestamp = DateTime.UtcNow
                });

            return successCount;
        }

        private static string MaskToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return string.Empty;
            return token.Length <= 10 ? "***" : token[..10] + "***";
        }

        public async Task<bool> SendNotificationToTokenAsync(string token, string title, string body, Dictionary<string, string>? data = null)
        {
            var projectId = _config["Firebase:ProjectId"];
            var serverKey = _config["Firebase:ServerKey"]; // รองรับ Legacy Key หรือ OAuth Token

            // หากไม่มีการคอนฟิก ตั้งค่า Simulation Mode และบันทึก Structured Telemetry เข้าสู่ระบบทันที
            if (string.IsNullOrEmpty(projectId) && string.IsNullOrEmpty(serverKey))
            {
                var maskedToken = MaskToken(token);
                _logger.LogInformation(
                    "FCM [SIMULATION MODE] - Simulated Push sent to Token {Token}. Title: {Title}, Body: {Body}. Detail: {@NotificationPayload}",
                    maskedToken,
                    title,
                    body,
                    new
                    {
                        Token = maskedToken,
                        Title = title,
                        Body = body,
                        Data = data ?? new Dictionary<string, string>(),
                        SimulatedAt = DateTime.UtcNow
                    });
                return true;
            }

            try
            {
                // ใช้ HttpClient ในการส่งยิง FCM (รองรับ Firebase Cloud Messaging HTTP v1 API)
                var client = _httpClientFactory.CreateClient();
                
                // รูปแบบ Legacy HTTP API
                if (!string.IsNullOrEmpty(serverKey))
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, "https://fcm.googleapis.com/fcm/send");
                    request.Headers.TryAddWithoutValidation("Authorization", $"key={serverKey}");
                    
                    var payload = new
                    {
                        to = token,
                        notification = new
                        {
                            title = title,
                            body = body,
                            sound = "default"
                        },
                        data = data
                    };

                    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var response = await client.SendAsync(request);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                    
                    var errorResponse = await response.Content.ReadAsStringAsync();
                    _logger.LogError("FCM request failed for token {Token}. Response: {Error}", MaskToken(token), errorResponse);
                    return false;
                }

                // รูปแบบ HTTP v1 API
                if (!string.IsNullOrEmpty(projectId))
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send");
                    
                    // หมายเหตุ: การใช้งาน v1 จำเป็นต้องสลับใช้ Google Access Token 
                    // โค้ดนี้ถูกเตรียมเผื่อสำหรับการทำโปรดักชันจริง
                    var accessToken = _config["Firebase:AccessToken"]; 
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    var payload = new
                    {
                        message = new
                        {
                            token = token,
                            notification = new
                            {
                                title = title,
                                body = body
                            },
                            data = data
                        }
                    };

                    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var response = await client.SendAsync(request);
                    
                    return response.IsSuccessStatusCode;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FCM delivery failed for token {Token}", MaskToken(token));
                return false;
            }
        }
    }
}


