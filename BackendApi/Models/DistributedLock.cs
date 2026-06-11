using System;
using System.ComponentModel.DataAnnotations;

namespace BackendApi.Models;

/// <summary>
/// Model สำหรับการทำ Distributed Lock บน PostgreSQL (Fallback เมื่อ Redis ล่ม)
/// </summary>
public class DistributedLock
{
    [Key]
    [MaxLength(250)]
    public string LockKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(250)]
    public string Value { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
}
