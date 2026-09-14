using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BackendApi.Models.DTOs
{
    /// <summary>
    /// DTO สำหรับส่งข้อมูล Order ไปยัง Frontend
    /// </summary>
    public class OrderDto
    {
        public string Id { get; set; } = string.Empty;
        public string TrackingCode { get; set; } = string.Empty;
        public string Status { get; set; } = "CREATED";
        public double? PickupLat { get; set; }
        public double? PickupLng { get; set; }
        public double? DropoffLat { get; set; }
        public double? DropoffLng { get; set; }
        public double DistanceKm { get; set; }
        public decimal DeliveryFee { get; set; }
        public DateTime ExpectedDeliveryTime { get; set; }
        public string? AssignedRiderId { get; set; }
        public string? CustomerId { get; set; }
        public string? ShopId { get; set; }

        public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();

        /// <summary>เวลาที่สร้างออเดอร์</summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>เวลาที่มอบหมายให้ Rider</summary>
        public DateTime? AssignedAt { get; set; }

        /// <summary>เวลาที่ส่งเสร็จสิ้น</summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>เส้นทางที่ผ่านถนนจริงเข้ารหัสแบบ Google Polyline</summary>
        public string? EncodedPolyline { get; set; }

        /// <summary>ระยะทางจริงของถนนจัดส่ง (เมตร)</summary>
        public double RouteDistanceMeters { get; set; }

        /// <summary>ระยะเวลาเดินทางจริงโดยประมาณ (วินาที)</summary>
        public double RouteDurationSeconds { get; set; }

        // ── Batch / Multi-stop ──────────────────────────────────────

        /// <summary>รหัสกลุ่มพ่วง (null = ออเดอร์เดี่ยว)</summary>
        public string? BatchGroupId { get; set; }

        /// <summary>ลำดับจัดส่งภายในกลุ่ม (0 = เดี่ยว, 1+ = ลำดับในกลุ่ม)</summary>
        public int BatchSequence { get; set; }

        /// <summary>จำนวนออเดอร์ทั้งหมดในกลุ่ม (0 หรือ 1 = เดี่ยว)</summary>
        public int BatchSize { get; set; }

        // ── Notes & Delivery Address ─────────────────────────────────
        public string? NoteToShop { get; set; }
        public string? NoteToRider { get; set; }
        public string? DeliveryAddress { get; set; }

        // ── Customer Review & Rating ─────────────────────────────────
        public int? Rating { get; set; }
        public string? ReviewComment { get; set; }
        public DateTime? ReviewedAt { get; set; }
    }

    /// <summary>
    /// DTO สำหรับสร้าง/แก้ไข Order (ใช้รับจาก Frontend)
    /// </summary>
    public class CreateOrderDto
    {
        [Range(-90.0, 90.0)]
        public double PickupLat { get; set; }
        [Range(-180.0, 180.0)]
        public double PickupLng { get; set; }
        [Range(-90.0, 90.0)]
        public double DropoffLat { get; set; }
        [Range(-180.0, 180.0)]
        public double DropoffLng { get; set; }
        public DateTime ExpectedDeliveryTime { get; set; }
        [MaxLength(64)]
        public string CustomerId { get; set; } = string.Empty;
        [MaxLength(64)]
        public string ShopId { get; set; } = string.Empty;
        public List<CreateOrderItemDto> Items { get; set; } = new List<CreateOrderItemDto>();

        // Notes & Delivery Address
        [MaxLength(500)]
        public string? NoteToShop { get; set; }
        [MaxLength(500)]
        public string? NoteToRider { get; set; }
        [MaxLength(500)]
        public string? DeliveryAddress { get; set; }
    }

    /// <summary>
    /// DTO สำหรับการอัปเดตสถานะออเดอร์โดย Rider หรือ Admin
    /// </summary>
    public class UpdateOrderStatusDto
    {
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// DTO สำหรับรายการสินค้าใน Order (ส่งออก)
    /// </summary>
    public class OrderItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string MenuItemId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public string? Notes { get; set; }
        public string? OptionsDescription { get; set; }
        public decimal TotalPrice { get; set; }
    }

    /// <summary>
    /// DTO สำหรับการรับข้อมูลสแนปช็อตสินค้าเพื่อสั่งซื้อออเดอร์
    /// </summary>
    public class CreateOrderItemDto
    {
        [Required]
        [MaxLength(64)]
        public string MenuItemId { get; set; } = string.Empty;
        [Range(1, 100)]
        public int Quantity { get; set; }
        [MaxLength(500)]
        public string? Notes { get; set; }
        [MaxLength(1000)]
        public string? OptionsDescription { get; set; }
    }

    /// <summary>
    /// DTO สำหรับลูกค้าส่งคะแนนและรีวิวออเดอร์หลังส่งมอบ
    /// </summary>
    public class SubmitOrderReviewDto
    {
        [Range(1, 5, ErrorMessage = "คะแนนต้องอยู่ระหว่าง 1 ถึง 5 ดาว")]
        public int Rating { get; set; }

        [MaxLength(1000, ErrorMessage = "ความคิดเห็นต้องไม่เกิน 1,000 ตัวอักษร")]
        public string? ReviewComment { get; set; }
    }

    /// <summary>
    /// DTO สำหรับสั่งเริ่มจัดส่งออเดอร์หลายใบพร้อมกันโดยแอดมิน (Manual Batching)
    /// </summary>
    public class BatchDispatchDto
    {
        public List<string> OrderIds { get; set; } = new();
        public string? RiderId { get; set; }
    }
}
