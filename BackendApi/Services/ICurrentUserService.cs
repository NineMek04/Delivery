using System;

namespace BackendApi.Services
{
    public interface ICurrentUserService
    {
        Guid? UserId { get; }
        string? UserName { get; }
        string? IpAddress { get; }
    }
}
