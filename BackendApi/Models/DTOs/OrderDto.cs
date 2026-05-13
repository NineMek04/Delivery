namespace BackendApi.Models.DTOs;

/// <summary>
/// DTO สำหรับส่งข้อมูล Order ไปยัง Frontend
/// </summary>
public class OrderDto
{
    /// <summary>รหัสออเดอร์ (GUID)</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>สถานะออเดอร์: PENDING, ASSIGNED, PICKED_UP, DELIVERING, COMPLETED, CANCELLED</summary>
    public string Status { get; set; } = "PENDING";

    /// <summary>ละติจูดจุดรับสินค้า (Pickup)</summary>
    public double? PickupLat { get; set; }

    /// <summary>ลองจิจูดจุดรับสินค้า (Pickup)</summary>
    public double? PickupLng { get; set; }

    /// <summary>ละติจูดจุดส่งสินค้า (Dropoff)</summary>
    public double? DropoffLat { get; set; }

    /// <summary>ลองจิจูดจุดส่งสินค้า (Dropoff)</summary>
    public double? DropoffLng { get; set; }

    /// <summary>เวลาที่คาดว่าจะส่งถึง</summary>
    public DateTime ExpectedDeliveryTime { get; set; }

    /// <summary>รหัสไรเดอร์ที่ได้รับมอบหมาย</summary>
    public string? AssignedRiderId { get; set; }
}

/// <summary>
/// DTO สำหรับสร้าง/แก้ไข Order (ใช้รับจาก Frontend)
/// </summary>
public class CreateOrderDto
{
    /// <summary>ละติจูดจุดรับสินค้า</summary>
    public double PickupLat { get; set; }

    /// <summary>ลองจิจูดจุดรับสินค้า</summary>
    public double PickupLng { get; set; }

    /// <summary>ละติจูดจุดส่งสินค้า</summary>
    public double DropoffLat { get; set; }

    /// <summary>ลองจิจูดจุดส่งสินค้า</summary>
    public double DropoffLng { get; set; }

    /// <summary>เวลาที่คาดว่าจะส่งถึง</summary>
    public DateTime ExpectedDeliveryTime { get; set; }
}
