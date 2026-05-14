using BackendApi.Core.Models;
using BackendApi.Models.DTOs;

namespace BackendApi.Services.Auth;

public interface IAuthService
{
    Task<ServiceResult<AuthResponse>> LoginAsync(
        LoginRequest request,
        string clientIp,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AuthResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<UserInfo>> GetSessionAsync(
        string? userId,
        CancellationToken cancellationToken = default);
}
