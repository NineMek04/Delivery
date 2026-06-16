using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BackendApi.Core.Constants;
using BackendApi.Core.Helpers;
using BackendApi.Core.Models;
using BackendApi.Core.Models.Response;
using BackendApi.Core.Models.Entities;
using BackendApi.Core.StateMachines;
using NetTopologySuite.Geometries;

namespace BackendApi.Models.Entities
{
    public class Rider : BaseSoftDeleteEntity<string>, ITrackableEntity
    {
        public long RefNumber { get; init; }

        [NotMapped]
        public string TrackingCode => TrackingCodeFormatter.Format(TrackingPrefixes.Rider, RefNumber);
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>สถานะปัจจุบัน (State Machine)</summary>
        public RiderState State { get; set; } = RiderState.IDLE;

        /// <summary>สถานะแบบ string สำหรับ backward compatibility</summary>
        [NotMapped]
        public string Status => State.ToString();

        // ฟิลด์สำคัญ: ใช้เก็บพิกัด GPS สำหรับให้ PostGIS คำนวณระยะทาง
        // Note: Redis เก็บ "latest" GPS สำหรับ real-time, PostGIS เก็บ "historical" (Source of Truth)
        [Column(TypeName = "geometry(Point, 4326)")]
        public Point? CurrentLocation { get; set; }

        // ── Presence & Heartbeat (แยก GPS กับ Heartbeat ตาม feedback) ──

        /// <summary>เวลาล่าสุดที่ Rider ส่ง Heartbeat (ยังออนไลน์ไหม)</summary>
        public DateTime? LastHeartbeat { get; set; }

        /// <summary>เวลาล่าสุดที่ GPS อัปเดต (ตำแหน่งยังนิ่งไหม)</summary>
        public DateTime? LastGpsUpdate { get; set; }
    }
}

