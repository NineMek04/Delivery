namespace BackendApi.Models.DTOs;

/// <summary>
/// DTO สำหรับส่งข้อมูลพิกัดและสถานะไรเดอร์จริงจาก Redis
/// </summary>
public class RiderLocationDto
{
    public string RiderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public double SnappedLat { get; set; }
    public double SnappedLng { get; set; }
    public bool IsSnapped { get; set; }
    public double SpeedKmh { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}