using System.ComponentModel.DataAnnotations.Schema;
using BackendApi.Core.Models;
using BackendApi.Core.StateMachines;
using NetTopologySuite.Geometries;

namespace BackendApi.Models
{
    public class Order : BaseSoftDeleteEntity<string>
    {
        /// <summary>สถานะปัจจุบันของ Order (State Machine)</summary>
        public OrderState State { get; set; } = OrderState.CREATED;

        /// <summary>สถานะแบบ string สำหรับ backward compatibility (mapped จาก State)</summary>
        [NotMapped]
        public string Status => State.ToString();

        // พิกัดร้านค้า (จุดรับของ)
        [Column(TypeName = "geometry(Point, 4326)")]
        public Point? PickupLocation { get; set; }

        // พิกัดลูกค้า (จุดส่งของ)
        [Column(TypeName = "geometry(Point, 4326)")]
        public Point? DropoffLocation { get; set; }

        /// <summary>ระยะทางโดยประมาณ (กิโลเมตร)</summary>
        public double DistanceKm { get; set; }

        /// <summary>ราคาค่าจัดส่ง</summary>
        public decimal DeliveryFee { get; set; }

        public DateTime ExpectedDeliveryTime { get; set; }

        public string? AssignedRiderId { get; set; }

        // ── Dispatch Offer Fields ──────────────────────────────────

        /// <summary>Offer ID ปัจจุบันที่ยิงไปให้ Rider (ใช้สำหรับ Idempotency)</summary>
        public string? CurrentOfferId { get; set; }

        /// <summary>Offer Version — เพิ่มทุกครั้งที่ Re-dispatch (ป้องกัน stale accept)</summary>
        public int OfferVersion { get; set; }

        /// <summary>เวลาที่ Offer จะหมดอายุ</summary>
        public DateTime? OfferExpiresAt { get; set; }

        /// <summary>ระยะเวลาที่ลูกค้ายอมรับได้ (SLA) หน่วยนาที</summary>
        public int SlaLimitMinutes { get; set; } = 30;

        // ── Timestamps (Additional to Auditable) ───────────────────

        public DateTime? AssignedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}