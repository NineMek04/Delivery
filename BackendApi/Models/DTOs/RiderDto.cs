namespace BackendApi.Models.DTOs;

/// <summary>
/// DTO สำหรับส่งข้อมูล Rider ไปยัง Frontend
/// </summary>
public class RiderDto
{
    /// <summary>รหัสไรเดอร์ (GUID)</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>โค้ดไรเดอร์สำหรับการอ้างอิงอักษร</summary>
    public string TrackingCode { get; set; } = string.Empty;

    /// <summary>ชื่อไรเดอร์</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>สถานะไรเดอร์: OFFLINE, IDLE, RESERVED, BUSY, STALE</summary>
    public string Status { get; set; } = "OFFLINE";

    /// <summary>ละติจูดปัจจุบัน</summary>
    public double? Lat { get; set; }

    /// <summary>ลองจิจูดปัจจุบัน</summary>
    public double? Lng { get; set; }

    /// <summary>เวลาที่อัปเดตตำแหน่งล่าสุด</summary>
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// DTO สำหรับสร้าง/แก้ไข Rider
/// </summary>
public class CreateRiderDto
{
    /// <summary>ชื่อไรเดอร์</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>ละติจูดเริ่มต้น (ไม่บังคับ)</summary>
    public double? Lat { get; set; }

    /// <summary>ลองจิจูดเริ่มต้น (ไม่บังคับ)</summary>
    public double? Lng { get; set; }
}
