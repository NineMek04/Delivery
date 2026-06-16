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

public sealed class AuthService : IAuthService
{
    private static readonly string[] AllowedRoles =
    [
        AuthConstants.AdminRole,
        AuthConstants.DispatcherRole,
        AuthConstants.RiderRole,
        AuthConstants.CustomerRole,
        AuthConstants.StorePartnerRole
    ];

    private static readonly string[] PublicRegistrationRoles =
    [
        AuthConstants.RiderRole,
        AuthConstants.CustomerRole,
        AuthConstants.StorePartnerRole
    ];

    private readonly string _dummyHash;

    private readonly ApplicationDbContext _dbContext;
    private readonly ITokenService _tokenService;
    private readonly LoginAttemptService _loginAttemptService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthService(
        ApplicationDbContext dbContext,
        ITokenService tokenService,
        LoginAttemptService loginAttemptService,
        IConfiguration configuration,
        ILogger<AuthService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
        _loginAttemptService = loginAttemptService;
        _configuration = configuration;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;

        _dummyHash = _configuration.GetValue<string>("Authentication:DummyPasswordHash") 
                     ?? PasswordHasher.HashPassword("dummy-password");
    }

    public async Task<ServiceResult<AuthResponse>> LoginAsync(
        LoginRequest request,
        string clientIp,
        CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(request.Email);
        
        // Find user first to determine lockout key (two-tier lockout key)
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        var lockoutKey = user != null 
            ? $"login:user:{user.Id}" 
            : $"login:email:{email}";

        if (_loginAttemptService.IsLockedOut(lockoutKey, out var retryAfter, out var wasUnlocked))
        {
            LogAuthEvent("AUTH_LOGIN", "ACCOUNT_LOCKOUT_ATTEMPT", user?.Id, email, user?.RiderId);

            _logger.LogWarning(
                "Login attempt blocked for {Email} (User: {UserId}) from {IP} because lockout is active (ACCOUNT_LOCKOUT_ATTEMPT)",
                email,
                user?.Id ?? "N/A",
                clientIp);

            var retrySecs = (int)Math.Ceiling(retryAfter.TotalSeconds);
            var lockedUntilUtc = DateTimeOffset.UtcNow.Add(retryAfter);

            return ServiceResult<AuthResponse>.FailureWithLockout(
                StatusCodes.Status429TooManyRequests,
                $"บัญชีถูกล็อกชั่วคราว กรุณาลองใหม่อีก {Math.Max(1, retryAfter.Minutes)} นาที",
                "ACCOUNT_LOCKED",
                retrySecs,
                lockedUntilUtc.ToString("o"));
        }

        if (wasUnlocked)
        {
            LogAuthEvent("AUTH_LOGIN", "ACCOUNT_UNLOCKED", user?.Id, email, user?.RiderId);
            _logger.LogInformation("Account {Email} (User: {UserId}) has been unlocked (lockout duration expired)", email, user?.Id ?? "N/A");
        }

        bool isValidPassword;
        if (user is null)
        {
            PasswordHasher.VerifyPassword(request.Password, _dummyHash);
            isValidPassword = false;
        }
        else
        {
            isValidPassword = PasswordHasher.VerifyPassword(request.Password, user.PasswordHash);
        }

        if (!isValidPassword)
        {
            SecurityMetrics.AuthFailedLoginsTotal.WithLabels("invalid_credentials").Inc();
            _loginAttemptService.RegisterFailure(lockoutKey);

            if (_loginAttemptService.IsLockedOut(lockoutKey, out var retryAfterAfterFailure, out _))
            {
                SecurityMetrics.AuthLockoutsTotal.Inc();
                LogAuthEvent("AUTH_LOGIN", "ACCOUNT_LOCKED", user?.Id, email, user?.RiderId);
                _logger.LogWarning("Account {Email} (User: {UserId}) has been locked for 15 minutes due to 5 failed login attempts", email, user?.Id ?? "N/A");

                var retrySecs = (int)Math.Ceiling(retryAfterAfterFailure.TotalSeconds);
                var lockedUntilUtc = DateTimeOffset.UtcNow.Add(retryAfterAfterFailure);

                return ServiceResult<AuthResponse>.FailureWithLockout(
                    StatusCodes.Status429TooManyRequests,
                    $"บัญชีถูกล็อกชั่วคราว กรุณาลองใหม่อีก {Math.Max(1, retryAfterAfterFailure.Minutes)} นาที",
                    "ACCOUNT_LOCKED",
                    retrySecs,
                    lockedUntilUtc.ToString("o"));
            }

            LogAuthEvent("AUTH_LOGIN", "AUTH_LOGIN_FAILED_INVALID_CREDENTIALS", user?.Id, email, user?.RiderId);

            return ServiceResult<AuthResponse>.Failure(
                StatusCodes.Status401Unauthorized,
                "อีเมลหรือรหัสผ่านไม่ถูกต้อง",
                "INVALID_CREDENTIALS");
        }

        if (!user!.IsActive)
        {
            LogAuthEvent("AUTH_LOGIN", "AUTH_LOGIN_FAILED_ACCOUNT_DISABLED", user.Id, user.Email, user.RiderId);

            return ServiceResult<AuthResponse>.Failure(
                StatusCodes.Status401Unauthorized,
                "บัญชีถูกระงับการใช้งาน",
                "ACCOUNT_DISABLED");
        }

        _loginAttemptService.Reset(lockoutKey);
        user.LastLoginAt = DateTime.UtcNow;

        var response = GenerateAuthResponse(user);

        // บันทึก Refresh Token ลง DB
        user.RefreshToken = HashRefreshToken(response.RefreshToken);
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(
            _configuration.GetValue("Authentication:RefreshTokenLifetimeDays", 7));

        await _dbContext.SaveChangesAsync(cancellationToken);

        LogAuthEvent("AUTH_LOGIN", "AUTH_LOGIN_SUCCESS", user.Id, user.Email, user.RiderId);

        return ServiceResult<AuthResponse>.Success(response, "เข้าสู่ระบบสำเร็จ");
    }

    public async Task<ServiceResult<AuthResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(request.Email);
        var role = NormalizeRole(request.Role);

        var emailExists = await _dbContext.Users
            .AnyAsync(u => u.Email == email, cancellationToken);

        if (emailExists)
        {
            LogAuthEvent("AUTH_REGISTER", "AUTH_REGISTER_FAILED_EMAIL_EXISTS", null, email);

            return ServiceResult<AuthResponse>.Failure(
                StatusCodes.Status409Conflict,
                "อีเมลนี้ถูกใช้งานแล้ว",
                "EMAIL_EXISTS");
        }

        if (!PublicRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            LogAuthEvent("AUTH_REGISTER", "AUTH_REGISTER_FAILED_INVALID_ROLE", null, email);

            return ServiceResult<AuthResponse>.Failure(
                StatusCodes.Status400BadRequest,
                $"บทบาทไม่ถูกต้องสำหรับการสมัครสาธารณะ (ใช้ได้: {string.Join(", ", PublicRegistrationRoles)})",
                "INVALID_ROLE");
        }

        var user = new User
        {
            Email = email,
            PasswordHash = PasswordHasher.HashPassword(request.Password),
            FullName = request.FullName.Trim(),
            Role = role
        };

        if (role.Equals(AuthConstants.RiderRole, StringComparison.OrdinalIgnoreCase))
        {
            var rider = new Rider
            {
                Name = user.FullName,
                State = BackendApi.Core.StateMachines.RiderState.OFFLINE
            };

            user.RiderId = rider.Id;
            _dbContext.Riders.Add(rider);
        }

        if (role.Equals(AuthConstants.StorePartnerRole, StringComparison.OrdinalIgnoreCase))
        {
            var shop = new Shop
            {
                Name = user.FullName,
                MenuName = "เมนูยอดนิยม",
                MenuPrice = 0,
                IsOpen = false
            };

            user.ShopId = shop.Id;
            _dbContext.Shops.Add(shop);
        }

        _dbContext.Users.Add(user);

        var response = GenerateAuthResponse(user);

        // บันทึก Refresh Token ลง DB
        user.RefreshToken = HashRefreshToken(response.RefreshToken);
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(
            _configuration.GetValue("Authentication:RefreshTokenLifetimeDays", 7));

        await _dbContext.SaveChangesAsync(cancellationToken);

        LogAuthEvent("AUTH_REGISTER", "AUTH_REGISTER_SUCCESS", user.Id, user.Email);

        return ServiceResult<AuthResponse>.Success(
            response,
            "ลงทะเบียนสำเร็จ",
            StatusCodes.Status201Created);
    }

    /// <summary>
    /// ใช้ Refresh Token เพื่อขอ Access Token + Refresh Token ชุดใหม่ (Token Rotation)
    /// </summary>
    public async Task<ServiceResult<AuthResponse>> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            LogAuthEvent("AUTH_REFRESH", "AUTH_REFRESH_FAILED_MISSING_TOKEN", null, null);

            return ServiceResult<AuthResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "กรุณาระบุ Refresh Token",
                "MISSING_REFRESH_TOKEN");
        }

        var hashedToken = HashRefreshToken(refreshToken);

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.RefreshToken == hashedToken, cancellationToken);

        if (user is null)
        {
            LogAuthEvent("AUTH_REFRESH", "AUTH_REFRESH_FAILED_INVALID_TOKEN", null, null);

            _logger.LogWarning("Refresh token not found in database (possible reuse attack)");
            return ServiceResult<AuthResponse>.Failure(
                StatusCodes.Status401Unauthorized,
                "Refresh Token ไม่ถูกต้อง",
                "INVALID_REFRESH_TOKEN");
        }

        if (user.RefreshTokenExpiresAt < DateTime.UtcNow)
        {
            // Refresh Token หมดอายุ → ลบออก → บังคับ re-login
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            await _dbContext.SaveChangesAsync(cancellationToken);

            LogAuthEvent("AUTH_REFRESH", "AUTH_REFRESH_FAILED_TOKEN_EXPIRED", user.Id, user.Email);

            _logger.LogWarning("Refresh token expired for user {Email}", user.Email);
            return ServiceResult<AuthResponse>.Failure(
                StatusCodes.Status401Unauthorized,
                "Refresh Token หมดอายุ กรุณาเข้าสู่ระบบใหม่",
                "REFRESH_TOKEN_EXPIRED");
        }

        if (!user.IsActive)
        {
            LogAuthEvent("AUTH_REFRESH", "AUTH_REFRESH_FAILED_ACCOUNT_DISABLED", user.Id, user.Email);

            return ServiceResult<AuthResponse>.Failure(
                StatusCodes.Status401Unauthorized,
                "บัญชีถูกระงับการใช้งาน",
                "ACCOUNT_DISABLED");
        }

        // Token Rotation: สร้าง Access Token + Refresh Token ชุดใหม่
        var response = GenerateAuthResponse(user);

        user.RefreshToken = HashRefreshToken(response.RefreshToken);
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(
            _configuration.GetValue("Authentication:RefreshTokenLifetimeDays", 7));

        await _dbContext.SaveChangesAsync(cancellationToken);

        LogAuthEvent("AUTH_REFRESH", "AUTH_REFRESH_SUCCESS", user.Id, user.Email);

        return ServiceResult<AuthResponse>.Success(response, "Token refreshed สำเร็จ");
    }

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


