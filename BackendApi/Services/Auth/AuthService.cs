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
        AuthConstants.RiderRole
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
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = GenerateAuthResponse(user);
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
                Status = "OFFLINE"
            };

            user.RiderId = rider.Id;
            _dbContext.Riders.Add(rider);
        }

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = GenerateAuthResponse(user);
        _logger.LogInformation("New user registered: {Email} as {Role}", user.Email, user.Role);

        return ServiceResult<AuthResponse>.Success(
            response,
            "ลงทะเบียนสำเร็จ",
            StatusCodes.Status201Created);
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

    private AuthResponse GenerateAuthResponse(User user)
    {
        var lifetimeHours = _configuration.GetValue("Authentication:SessionLifetimeHours", 24);
        var expiresAt = DateTime.UtcNow.AddHours(lifetimeHours);

        var subject = new TokenSubject(user.Id, user.Email, user.FullName, user.Role);
        var accessToken = _tokenService.CreateAccessToken(subject, expiresAt);

        return new AuthResponse
        {
            AccessToken = accessToken,
            ExpiresAt = expiresAt,
            User = MapUserInfo(user)
        };
    }

    private static UserInfo MapUserInfo(User user) =>
        new()
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role,
            RiderId = user.RiderId
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
