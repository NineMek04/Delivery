using System.ComponentModel.DataAnnotations;

namespace BackendApi.Models.DTOs;

/// <summary>
/// DTO สำหรับ Login Request
/// </summary>
public class LoginRequest
{
    /// <summary>อีเมลผู้ใช้</summary>
    [Required(ErrorMessage = "กรุณากรอกอีเมล")]
    [EmailAddress(ErrorMessage = "รูปแบบอีเมลไม่ถูกต้อง")]
    public string Email { get; set; } = string.Empty;

    /// <summary>รหัสผ่าน</summary>
    [Required(ErrorMessage = "กรุณากรอกรหัสผ่าน")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// DTO สำหรับ Register Request
/// </summary>
public class RegisterRequest
{
    /// <summary>อีเมลผู้ใช้</summary>
    [Required(ErrorMessage = "กรุณากรอกอีเมล")]
    [EmailAddress(ErrorMessage = "รูปแบบอีเมลไม่ถูกต้อง")]
    public string Email { get; set; } = string.Empty;

    /// <summary>รหัสผ่าน (ขั้นต่ำ 6 ตัวอักษร)</summary>
    [Required(ErrorMessage = "กรุณากรอกรหัสผ่าน")]
    [MinLength(6, ErrorMessage = "รหัสผ่านต้องมีอย่างน้อย 6 ตัวอักษร")]
    public string Password { get; set; } = string.Empty;

    /// <summary>ชื่อ-นามสกุล</summary>
    [Required(ErrorMessage = "กรุณากรอกชื่อ-นามสกุล")]
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>บทบาท: Admin, Dispatcher, Rider (default: Rider)</summary>
    [MaxLength(20)]
    public string Role { get; set; } = "Rider";
}

/// <summary>
/// DTO สำหรับ Refresh Token Request
/// </summary>
public class RefreshTokenRequest
{
    /// <summary>Refresh Token ที่ได้จาก Login/Register</summary>
    [Required(ErrorMessage = "กรุณาระบุ Refresh Token")]
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// DTO สำหรับ Auth Response — ส่งกลับหลัง Login / Register / Refresh สำเร็จ
/// </summary>
public class AuthResponse
{
    /// <summary>JWT Access Token</summary>
    [System.Text.Json.Serialization.JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Refresh Token — ใช้ขอ Access Token ใหม่เมื่อหมดอายุ</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>เวลาหมดอายุของ Access Token (UTC)</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>ข้อมูลผู้ใช้</summary>
    public UserInfo User { get; set; } = new();
}

/// <summary>
/// ข้อมูลผู้ใช้ที่ส่งกลับ (ไม่มี password)
/// </summary>
public class UserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? RiderId { get; set; }
}
