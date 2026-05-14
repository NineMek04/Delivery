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

    /// <summary>
    /// ใช้ Refresh Token เพื่อขอ Access Token + Refresh Token ชุดใหม่
    /// </summary>
    Task<ServiceResult<AuthResponse>> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<UserInfo>> GetSessionAsync(
        string? userId,
        CancellationToken cancellationToken = default);
}
