using System.ComponentModel.DataAnnotations;
using BackendApi.Core.Models;

namespace BackendApi.Models;

/// <summary>
/// ผู้ใช้ระบบ — รองรับ Admin, Dispatcher และ Rider
/// </summary>
public class User : BaseSoftDeleteEntity<string>
{
    [Required, MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Role { get; set; } = "Rider"; // Admin, Dispatcher, Rider

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// เชื่อมกับ Rider entity (nullable — มีเฉพาะ role Rider)
    /// </summary>
    public string? RiderId { get; set; }

    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Refresh Token (hashed) — ใช้ขอ Access Token ใหม่เมื่อหมดอายุ
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// เวลาหมดอายุของ Refresh Token
    /// </summary>
    public DateTime? RefreshTokenExpiresAt { get; set; }
}
