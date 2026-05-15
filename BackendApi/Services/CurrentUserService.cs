using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Services
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Guid? UserId
        {
            get
            {
                var userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                return Guid.TryParse(userId, out var result) ? result : null;
            }
        }

        public string? UserName => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name) 
                                   ?? _httpContextAccessor.HttpContext?.User?.FindFirstValue("name");

        public string? IpAddress
        {
            get
            {
                var context = _httpContextAccessor.HttpContext;
                if (context == null) return null;

                // Safe IP Parsing for Proxies
                string? ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                
                if (!string.IsNullOrEmpty(ip))
                {
                    // Split and take the first one (original client)
                    return ip.Split(',')[0].Trim();
                }

                return context.Connection.RemoteIpAddress?.ToString();
            }
        }
    }
}
