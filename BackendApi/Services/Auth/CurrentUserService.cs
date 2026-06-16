using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BackendApi.Services.Auth
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
                return context?.Connection.RemoteIpAddress?.ToString();
            }
        }
    }
}

