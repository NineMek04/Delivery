using System.Security.Cryptography;
using BackendApi.Core.Models;
using BackendApi.Core.Models.Response;
using BackendApi.Core.Models.Entities;
using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using BackendApi.Security.Models;
using BackendApi.Security.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Services.Auth;

public sealed partial class AuthService
{
    public async Task<ServiceResult<UserInfo>> GetSessionAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<UserInfo>.Failure(
                StatusCodes.Status401Unauthorized,
                "ไม่พบ session",
                "NO_SESSION");
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return ServiceResult<UserInfo>.Failure(
                StatusCodes.Status401Unauthorized,
                "session หมดอายุ",
                "SESSION_EXPIRED");
        }

        return ServiceResult<UserInfo>.Success(MapUserInfo(user));
    }

    public async Task<ServiceResult<bool>> ChangePasswordAsync(
        string? userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<bool>.Failure(
                StatusCodes.Status401Unauthorized,
                "ไม่พบ session",
                "NO_SESSION");
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return ServiceResult<bool>.Failure(
                StatusCodes.Status401Unauthorized,
                "ผู้ใช้งานไม่ถูกต้องหรือถูกระงับการใช้งาน",
                "INVALID_USER");
        }

        if (!PasswordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
        {
            return ServiceResult<bool>.Failure(
                StatusCodes.Status400BadRequest,
                "รหัสผ่านปัจจุบันไม่ถูกต้อง",
                "INVALID_CURRENT_PASSWORD");
        }

        user.PasswordHash = PasswordHasher.HashPassword(request.NewPassword);
        
        // Revoke refresh token when password changes
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {Email} changed password successfully. RiderId: {RiderId}", user.Email, user.RiderId ?? "N/A");

        return ServiceResult<bool>.Success(true, "เปลี่ยนรหัสผ่านสำเร็จ");
    }

    public async Task<ServiceResult<bool>> LogoutAsync(
        string? userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            LogAuthEvent("AUTH_LOGOUT", "AUTH_LOGOUT_SUCCESS", null, null);
            return ServiceResult<bool>.Success(true, "ออกจากระบบสำเร็จ (ไม่มี session)");
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user != null)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
            
            LogAuthEvent("AUTH_LOGOUT", "AUTH_LOGOUT_SUCCESS", user.Id, user.Email);
        }
        else
        {
            LogAuthEvent("AUTH_LOGOUT", "AUTH_LOGOUT_SUCCESS", userId, null);
        }

        return ServiceResult<bool>.Success(true, "ออกจากระบบสำเร็จ");
    }

    // ── Private helpers ──────────────────────────────────────────────

    private void LogAuthEvent(
        string operation,
        string result,
        string? userId,
        string? email = null,
        string? riderId = null,
        string? orderId = null)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var clientIp = httpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
        var clientType = httpContext?.Request?.Headers["X-Client-Type"].ToString();
        if (string.IsNullOrEmpty(clientType)) clientType = "Unknown";
        var correlationId = httpContext?.Items["CorrelationId"]?.ToString() ?? "unknown";

        _logger.LogInformation(
            "AuthEvent {Operation} {Result} {UserId} {Email} {ClientType} {IP} {CorrelationId} {OrderId} {RiderId}",
            operation,
            result,
            userId ?? "N/A",
            email ?? "N/A",
            clientType,
            clientIp,
            correlationId,
            orderId ?? "N/A",
            riderId ?? "N/A");
    }

    private AuthResponse GenerateAuthResponse(User user)
    {
        var lifetimeHours = _configuration.GetValue("Authentication:SessionLifetimeHours", 24);
        var expiresAt = DateTime.UtcNow.AddHours(lifetimeHours);

        var subject = new TokenSubject(user.Id, user.Email, user.FullName, user.Role, user.ShopId);
        var accessToken = _tokenService.CreateAccessToken(subject, expiresAt);
        var refreshToken = GenerateRefreshToken();

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            User = MapUserInfo(user)
        };
    }

    /// <summary>
    /// สร้าง cryptographically-secure random Refresh Token (Base64)
    /// </summary>
    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    /// <summary>
    /// Hash Refresh Token ด้วย SHA-256 ก่อนเก็บลง DB เพื่อความปลอดภัย
    /// </summary>
    private static string HashRefreshToken(string token)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    private static UserInfo MapUserInfo(User user) =>
        new()
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role,
            RiderId = user.RiderId,
            ShopId = user.ShopId
        };

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static string NormalizeRole(string role)
    {
        var normalizedRole = role.Trim();

        return AllowedRoles.FirstOrDefault(allowedRole =>
                   allowedRole.Equals(normalizedRole, StringComparison.OrdinalIgnoreCase))
               ?? normalizedRole;
    }
}
