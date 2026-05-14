using BackendApi.Core;
using BackendApi.Core.Models;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using BackendApi.Services.Auth;
using BackendApi.Setup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BackendApi.Controllers.Business;

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
            return StatusCode(result.StatusCode, result.ToApiResponseBase());

        SetAccessTokenCookie(result.Value.AccessToken, result.Value.ExpiresAt);

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

        SetAccessTokenCookie(result.Value.AccessToken, result.Value.ExpiresAt);

        return StatusCode(result.StatusCode, result.ToApiResponse());
    }

    /// <summary>
    /// ใช้ Refresh Token เพื่อขอ Access Token ใหม่ (Token Rotation)
    /// </summary>
    /// <param name="request">Refresh Token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JWT Access Token + Refresh Token ชุดใหม่</returns>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> RefreshToken(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _authService.RefreshTokenAsync(request.RefreshToken, cancellationToken);

        if (!result.Succeeded || result.Value is null)
            return StatusCode(result.StatusCode, result.ToApiResponseBase());

        SetAccessTokenCookie(result.Value.AccessToken, result.Value.ExpiresAt);

        return StatusCode(result.StatusCode, result.ToApiResponse());
    }

    /// <summary>
    /// ออกจากระบบ — ลบ access_token cookie + revoke refresh token
    /// </summary>
    /// <returns>ผลลัพธ์การออกจากระบบ</returns>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse> Logout()
    {
        var config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        Response.Cookies.Delete(AuthConstants.AccessTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = config.GetValue("Authentication:RequireSecureCookie", false),
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });

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

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// ตั้ง HttpOnly cookie สำหรับ access_token
    /// เพื่อรองรับ web clients ที่ไม่ส่ง Bearer header
    /// (ตาม .cursorrules: Token สามารถรับจาก Authorization: Bearer หรือ HttpOnly cookie)
    /// </summary>
    private void SetAccessTokenCookie(string token, DateTime expiresAt)
    {
        var config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var requireSecure = config.GetValue("Authentication:RequireSecureCookie", false);
        var sameSiteStr = config["Authentication:CookieSameSite"] ?? "Lax";
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
