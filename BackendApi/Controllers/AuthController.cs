using BackendApi.Core.Models;
using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using BackendApi.Setup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Controllers;

/// <summary>
/// จัดการ Authentication — Login / Register / Logout / Session
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ITokenService _tokenService;
    private readonly LoginAttemptService _loginAttemptService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        ApplicationDbContext dbContext,
        ITokenService tokenService,
        LoginAttemptService loginAttemptService,
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
        _loginAttemptService = loginAttemptService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// เข้าสู่ระบบด้วยอีเมลและรหัสผ่าน
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting(SecurityConfiguration.AuthRateLimitPolicy)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var lockoutKey = $"login:{clientIp}:{request.Email}";

        // ตรวจสอบ lockout
        if (_loginAttemptService.IsLockedOut(lockoutKey, out var retryAfter))
        {
            _logger.LogWarning("Login attempt blocked for {Email} from {IP} — lockout active", request.Email, clientIp);
            return StatusCode(StatusCodes.Status429TooManyRequests,
                ApiResponse.Fail(
                    $"บัญชีถูกล็อกชั่วคราว กรุณาลองใหม่ในอีก {retryAfter.Minutes} นาที",
                    code: "ACCOUNT_LOCKED"));
        }

        // ค้นหาผู้ใช้
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

        if (user is null || !PasswordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            _loginAttemptService.RegisterFailure(lockoutKey);
            return Unauthorized(ApiResponse.Fail("อีเมลหรือรหัสผ่านไม่ถูกต้อง", code: "INVALID_CREDENTIALS"));
        }

        if (!user.IsActive)
        {
            return Unauthorized(ApiResponse.Fail("บัญชีถูกระงับการใช้งาน", code: "ACCOUNT_DISABLED"));
        }

        // Reset lockout counter on success
        _loginAttemptService.Reset(lockoutKey);

        // อัปเดต last login
        user.LastLoginAt = DateTime.UtcNow;
        _dbContext.Users.Update(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // สร้าง JWT token
        var authResponse = GenerateAuthResponse(user);

        // ตั้ง HttpOnly Cookie
        SetAccessTokenCookie(authResponse.AccessToken, authResponse.ExpiresAt);

        _logger.LogInformation("User {Email} logged in successfully", user.Email);
        return Ok(ApiResponse<AuthResponse>.Ok(authResponse, "เข้าสู่ระบบสำเร็จ"));
    }

    /// <summary>
    /// ลงทะเบียนผู้ใช้ใหม่
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting(SecurityConfiguration.AuthRateLimitPolicy)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        // ตรวจสอบอีเมลซ้ำ
        var emailExists = await _dbContext.Users
            .AnyAsync(u => u.Email == request.Email, cancellationToken);

        if (emailExists)
        {
            return Conflict(ApiResponse.Fail("อีเมลนี้ถูกใช้งานแล้ว", code: "EMAIL_EXISTS"));
        }

        // Validate role
        var allowedRoles = new[] { AuthConstants.AdminRole, AuthConstants.DispatcherRole, AuthConstants.RiderRole };
        if (!allowedRoles.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest(ApiResponse.Fail($"บทบาทไม่ถูกต้อง (ใช้ได้: {string.Join(", ", allowedRoles)})", code: "INVALID_ROLE"));
        }

        var user = new User
        {
            Email = request.Email,
            PasswordHash = PasswordHasher.HashPassword(request.Password),
            FullName = request.FullName,
            Role = request.Role
        };

        // ถ้าเป็น Rider → สร้าง Rider entity ที่เชื่อมกันด้วย
        if (request.Role.Equals(AuthConstants.RiderRole, StringComparison.OrdinalIgnoreCase))
        {
            var rider = new Rider
            {
                Name = request.FullName,
                Status = "OFFLINE"
            };
            _dbContext.Riders.Add(rider);
            await _dbContext.SaveChangesAsync(cancellationToken); // save เพื่อให้ได้ rider.Id
            user.RiderId = rider.Id;
        }

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var authResponse = GenerateAuthResponse(user);
        SetAccessTokenCookie(authResponse.AccessToken, authResponse.ExpiresAt);

        _logger.LogInformation("New user registered: {Email} as {Role}", user.Email, user.Role);
        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<AuthResponse>.Ok(authResponse, "ลงทะเบียนสำเร็จ"));
    }

    /// <summary>
    /// ออกจากระบบ — ลบ access token cookie
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse> Logout()
    {
        Response.Cookies.Delete(AuthConstants.AccessTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = _configuration.GetValue("Authentication:RequireSecureCookie", false),
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });

        return Ok(ApiResponse.Ok("ออกจากระบบสำเร็จ"));
    }

    /// <summary>
    /// ดึงข้อมูล session ปัจจุบัน
    /// </summary>
    [HttpGet("session")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<UserInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<UserInfo>>> GetSession(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(ApiResponse.Fail("ไม่พบ session", code: "NO_SESSION"));
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return Unauthorized(ApiResponse.Fail("session หมดอายุ", code: "SESSION_EXPIRED"));
        }

        var userInfo = new UserInfo
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role,
            RiderId = user.RiderId
        };

        return Ok(ApiResponse<UserInfo>.Ok(userInfo));
    }

    // --- Private Helpers ---

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
            User = new UserInfo
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role,
                RiderId = user.RiderId
            }
        };
    }

    private void SetAccessTokenCookie(string token, DateTime expiresAt)
    {
        var requireSecure = _configuration.GetValue("Authentication:RequireSecureCookie", false);
        var sameSiteStr = _configuration["Authentication:CookieSameSite"] ?? "Lax";
        var sameSite = sameSiteStr.Equals("None", StringComparison.OrdinalIgnoreCase)
            ? SameSiteMode.None
            : sameSiteStr.Equals("Strict", StringComparison.OrdinalIgnoreCase)
                ? SameSiteMode.Strict
                : SameSiteMode.Lax;

        Response.Cookies.Append(AuthConstants.AccessTokenCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = requireSecure,
            SameSite = sameSite,
            Expires = expiresAt,
            Path = "/"
        });
    }
}
