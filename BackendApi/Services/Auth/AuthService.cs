using System.Security.Cryptography;
using BackendApi.Core.Models;
using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using BackendApi.Security;
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

    private readonly ApplicationDbContext _dbContext;
    private readonly ITokenService _tokenService;
    private readonly LoginAttemptService _loginAttemptService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        ApplicationDbContext dbContext,
        ITokenService tokenService,
        LoginAttemptService loginAttemptService,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
        _loginAttemptService = loginAttemptService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ServiceResult<AuthResponse>> LoginAsync(
        LoginRequest request,
        string clientIp,
        CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(request.Email);
        var lockoutKey = $"login:{clientIp}:{email}";

        if (_loginAttemptService.IsLockedOut(lockoutKey, out var retryAfter))
        {
            _logger.LogWarning(
                "Login attempt blocked for {Email} from {IP} because lockout is active",
                email,
                clientIp);

            return ServiceResult<AuthResponse>.Failure(
                StatusCodes.Status429TooManyRequests,
                $"บัญชีถูกล็อกชั่วคราว กรุณาลองใหม่อีก {Math.Max(1, retryAfter.Minutes)} นาที",
                "ACCOUNT_LOCKED");
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null || !PasswordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            _loginAttemptService.RegisterFailure(lockoutKey);

            return ServiceResult<AuthResponse>.Failure(
                StatusCodes.Status401Unauthorized,
                "อีเมลหรือรหัสผ่านไม่ถูกต้อง",
                "INVALID_CREDENTIALS");
        }

        if (!user.IsActive)
        {
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

        _logger.LogInformation("User {Email} logged in successfully", user.Email);

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
            return ServiceResult<AuthResponse>.Failure(
                StatusCodes.Status409Conflict,
                "อีเมลนี้ถูกใช้งานแล้ว",
                "EMAIL_EXISTS");
        }

        if (!AllowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            return ServiceResult<AuthResponse>.Failure(
                StatusCodes.Status400BadRequest,
                $"บทบาทไม่ถูกต้อง (ใช้ได้: {string.Join(", ", AllowedRoles)})",
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

        _logger.LogInformation("New user registered: {Email} as {Role}", user.Email, user.Role);

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

            _logger.LogWarning("Refresh token expired for user {Email}", user.Email);
            return ServiceResult<AuthResponse>.Failure(
                StatusCodes.Status401Unauthorized,
                "Refresh Token หมดอายุ กรุณาเข้าสู่ระบบใหม่",
                "REFRESH_TOKEN_EXPIRED");
        }

        if (!user.IsActive)
        {
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

        _logger.LogInformation("Token refreshed for user {Email}", user.Email);

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
            return ServiceResult<bool>.Success(true, "ออกจากระบบสำเร็จ (ไม่มี session)");
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user != null)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("User {Email} logged out and refresh token was revoked.", user.Email);
        }

        return ServiceResult<bool>.Success(true, "ออกจากระบบสำเร็จ");
    }

    // ── Private helpers ──────────────────────────────────────────────

    private AuthResponse GenerateAuthResponse(User user)
    {
        var lifetimeHours = _configuration.GetValue("Authentication:SessionLifetimeHours", 24);
        var expiresAt = DateTime.UtcNow.AddHours(lifetimeHours);

        var subject = new TokenSubject(user.Id, user.Email, user.FullName, user.Role);
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
