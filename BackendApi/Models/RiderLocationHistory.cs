using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace BackendApi.Models
{
    /// <summary>
    /// เก็บประวัติการเดินทางของ Rider (PostGIS - The Ledger)
    /// ใช้สำหรับแสดงเส้นทางย้อนหลัง, คำนวณระยะทาง, และ Analytics
    /// </summary>
    public class RiderLocationHistory
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string RiderId { get; set; } = string.Empty;

        [Column(TypeName = "geometry(Point, 4326)")]
        public Point Location { get; set; } = null!;

        public DateTime RecordedAt { get; set; }

        // สามารถเพิ่ม OrderId ด้วยถ้ารู้ว่าพิกัดนี้อยู่ในช่วงที่กำลังวิ่งงานไหน
        public string? OrderId { get; set; }
    }
}
