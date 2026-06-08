using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BackendApi.Services.Auth
{
    public sealed class AdminAuditService : IAuditLogger
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AdminAuditService> _logger;

        public AdminAuditService(IHttpContextAccessor httpContextAccessor, ILogger<AdminAuditService> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public void LogAdminAction(
            string action,
            string targetType,
            string targetId,
            string? beforeState,
            string? afterState)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var userId = httpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var role = httpContext?.User?.FindFirstValue(ClaimTypes.Role) ?? "Admin";

            // Extract client IP address safely
            string? ip = httpContext?.Request?.Headers["X-Forwarded-For"].FirstOrDefault();
            if (string.IsNullOrEmpty(ip))
            {
                ip = httpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
            }
            else
            {
                ip = ip.Split(',')[0].Trim();
            }

            var correlationId = httpContext?.Items["CorrelationId"]?.ToString() ?? "unknown";
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            var logPayload = new
            {
                EventType = "ADMIN_ACTION",
                UserId = userId,
                Role = role,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                BeforeState = beforeState ?? "N/A",
                AfterState = afterState ?? "N/A",
                CorrelationId = correlationId,
                Ip = ip,
                Timestamp = timestamp
            };

            var jsonLog = JsonSerializer.Serialize(logPayload);
            _logger.LogInformation("ADMIN_AUDIT: {AdminAuditLog}", jsonLog);
        }
    }
}
