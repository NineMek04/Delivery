using System;

namespace BackendApi.Services.Auth
{
    public interface ICurrentUserService
    {
        Guid? UserId { get; }
        string? UserName { get; }
        string? IpAddress { get; }
    }
}

