using BackendApi.Core;
using BackendApi.Core.Models;
using BackendApi.Core.Models.Response;
using BackendApi.Core.Models.Entities;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using BackendApi.Security.Models;
using BackendApi.Security.Services;
using BackendApi.Services.Auth;
using BackendApi.Services.Telemetry;
using BackendApi.Setup;
using BackendApi.Setup.Middlewares;
using BackendApi.Setup.Configuration;
using BackendApi.Setup.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BackendApi.Controllers.Auth;

/// <summary>
/// จัดการ Authentication — Login / Register / Logout / Session
/// </summary>
/// <remarks>
/// Controller นี้อยู่ใน Business/ เพราะมี logic ซับซ้อน (cookie, rate limit, lockout)
/// ไม่เหมาะใช้ CrudControllerBase — ให้เขียน Action แบบ Explicit
/// 
/// Route: api/v1/auth (สืบทอดจาก DeliveryControllerBase → api/v1/[controller])
/// </remarks>
public class AuthController : DeliveryControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// เข้าสู่ระบบด้วยอีเมลและรหัสผ่าน
    /// </summary>
    /// <param name="request">ข้อมูล Login (Email + Password)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JWT Access Token + ข้อมูลผู้ใช้</returns>
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
        var result = await _authService.LoginAsync(request, clientIp, cancellationToken);

        if (!result.Succeeded || result.Value is null)
        {
            if (result.Code == "ACCOUNT_LOCKED")
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    success = false,
                    message = result.Message,
                    code = "ACCOUNT_LOCKED",
                    error = "ACCOUNT_LOCKED",
                    retryAfterSeconds = result.RetryAfterSeconds ?? 900,
                    retryAfter = result.LockedUntil ?? DateTimeOffset.UtcNow.AddMinutes(15).ToString("o")
                });
            }
            return StatusCode(result.StatusCode, result.ToApiResponseBase());
        }

        SetAuthCookiesIfDashboard(result.Value.AccessToken, result.Value.RefreshToken, result.Value.ExpiresAt);

        return StatusCode(result.StatusCode, result.ToApiResponse());
    }

    /// <summary>
    /// ลงทะเบียนผู้ใช้ใหม่
    /// </summary>
    /// <param name="request">ข้อมูลลงทะเบียน (Email, Password, FullName, Role)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JWT Access Token + ข้อมูลผู้ใช้ที่สร้าง</returns>
    [HttpPost("register")]
    [EnableRateLimiting(SecurityConfiguration.AuthRateLimitPolicy)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _authService.RegisterAsync(request, cancellationToken);

        if (!result.Succeeded || result.Value is null)
            return StatusCode(result.StatusCode, result.ToApiResponseBase());

        SetAuthCookiesIfDashboard(result.Value.AccessToken, result.Value.RefreshToken, result.Value.ExpiresAt);

        return StatusCode(result.StatusCode, result.ToApiResponse());
    }

    /// <summary>
    /// ใช้ Refresh Token เพื่อขอ Access Token ใหม่ (Token Rotation)
    /// </summary>
    /// <param name="request">Refresh Token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JWT Access Token + Refresh Token ชุดใหม่</returns>
    [HttpPost("refresh")]
    [EnableRateLimiting(SecurityConfiguration.AuthRateLimitPolicy)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> RefreshToken(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        string? refreshToken = request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            Request.Cookies.TryGetValue("refresh_token", out refreshToken);
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return BadRequest(ApiResponse.Fail("กรุณาระบุ Refresh Token"));
        }

        var result = await _authService.RefreshTokenAsync(refreshToken, cancellationToken);

        if (!result.Succeeded || result.Value is null)
        {
            OperationalMetrics.TokenRefreshFailures
                .WithLabels(result.Code ?? $"http_{result.StatusCode}")
                .Inc();
            return StatusCode(result.StatusCode, result.ToApiResponseBase());
        }

        SetAuthCookiesIfDashboard(result.Value.AccessToken, result.Value.RefreshToken, result.Value.ExpiresAt);

        return StatusCode(result.StatusCode, result.ToApiResponse());
    }

    /// <summary>
    /// ออกจากระบบ — ลบ access_token cookie + revoke refresh token
    /// </summary>
    /// <returns>ผลลัพธ์การออกจากระบบ</returns>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Logout(CancellationToken cancellationToken = default)
    {
        await _authService.LogoutAsync(CurrentUserId, cancellationToken);
        DeleteAuthCookies();

        return Ok(ApiResponse.Ok("ออกจากระบบสำเร็จ"));
    }

    /// <summary>
    /// ดึงข้อมูล session ปัจจุบัน (ต้อง login แล้ว)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ข้อมูลผู้ใช้ที่ login อยู่</returns>
    [HttpGet("session")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<UserInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<UserInfo>>> GetSession(
        CancellationToken cancellationToken = default)
    {
        var result = await _authService.GetSessionAsync(CurrentUserId, cancellationToken);

        if (!result.Succeeded || result.Value is null)
            return StatusCode(result.StatusCode, result.ToApiResponseBase());

        return Ok(result.ToApiResponse());
    }

    /// <summary>
    /// เปลี่ยนรหัสผ่านของผู้ใช้งานปัจจุบัน
    /// </summary>
    /// <param name="request">รหัสผ่านปัจจุบันและรหัสผ่านใหม่</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ผลลัพธ์การเปลี่ยนรหัสผ่าน</returns>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse>> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _authService.ChangePasswordAsync(CurrentUserId, request, cancellationToken);

        if (!result.Succeeded)
            return StatusCode(result.StatusCode, result.ToApiResponseBase());

        DeleteAuthCookies();

        return Ok(ApiResponse.Ok("เปลี่ยนรหัสผ่านสำเร็จ กรุณาเข้าสู่ระบบใหม่อีกครั้ง"));
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// ตั้ง HttpOnly cookie สำหรับ access_token และ refresh_token สำหรับ client-type Dashboard
    /// </summary>
    private void SetAuthCookiesIfDashboard(string accessToken, string refreshToken, DateTime expiresAt)
    {
        var clientType = Request.Headers["X-Client-Type"].ToString();
        if (clientType != "Dashboard")
        {
            return;
        }

        var config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var requireSecure = config.GetValue("Authentication:RequireSecureCookie", false);
        var sameSite = SameSiteMode.Lax;

        Response.Cookies.Append(AuthConstants.AccessTokenCookieName, accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = requireSecure,
            SameSite = sameSite,
            Expires = expiresAt,
            Path = "/"
        });

        var refreshLifetimeDays = config.GetValue("Authentication:RefreshTokenLifetimeDays", 7);
        Response.Cookies.Append("refresh_token", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = requireSecure,
            SameSite = sameSite,
            Expires = DateTimeOffset.UtcNow.AddDays(refreshLifetimeDays),
            Path = "/"
        });

        // CSRF Rotation: Generate a new XSRF-TOKEN on every Login/Refresh
        var xsrfToken = Guid.NewGuid().ToString("N");
        Response.Cookies.Append("XSRF-TOKEN", xsrfToken, new CookieOptions
        {
            HttpOnly = false, // Critical: Angular needs to read this
            Secure = requireSecure,
            SameSite = sameSite,
            Expires = expiresAt,
            Path = "/"
        });
    }

    private void DeleteAuthCookies()
    {
        var config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var requireSecure = config.GetValue("Authentication:RequireSecureCookie", false);

        Response.Cookies.Delete(AuthConstants.AccessTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = requireSecure,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });

        Response.Cookies.Delete("refresh_token", new CookieOptions
        {
            HttpOnly = true,
            Secure = requireSecure,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });

        Response.Cookies.Delete("XSRF-TOKEN", new CookieOptions
        {
            HttpOnly = false,
            Secure = requireSecure,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });
    }
}


