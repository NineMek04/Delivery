using System.ComponentModel.DataAnnotations;

namespace BackendApi.Models;

/// <summary>
/// ผู้ใช้ระบบ — รองรับ Admin, Dispatcher และ Rider
/// </summary>
public class User
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
