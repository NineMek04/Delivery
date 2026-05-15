namespace BackendApi.Models.DTOs;

/// <summary>
/// DTO สำหรับส่งข้อมูล Order ไปยัง Frontend
/// </summary>
public class OrderDto
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public double? PickupLat { get; set; }
    public double? PickupLng { get; set; }
    public double? DropoffLat { get; set; }
    public double? DropoffLng { get; set; }
    public double DistanceKm { get; set; }
    public decimal DeliveryFee { get; set; }
    public DateTime ExpectedDeliveryTime { get; set; }
    public string? AssignedRiderId { get; set; }
}

/// <summary>
/// DTO สำหรับสร้าง/แก้ไข Order (ใช้รับจาก Frontend)
/// </summary>
public class CreateOrderDto
{
    public double PickupLat { get; set; }
    public double PickupLng { get; set; }
    public double DropoffLat { get; set; }
    public double DropoffLng { get; set; }
    public DateTime ExpectedDeliveryTime { get; set; }
}

/// <summary>
/// DTO สำหรับการอัปเดตสถานะออเดอร์โดย Rider หรือ Admin
/// </summary>
public class UpdateOrderStatusDto
{
    public string Status { get; set; } = string.Empty;
}
