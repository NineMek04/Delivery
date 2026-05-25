using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BackendApi.Core.Constants;
using BackendApi.Core.Helpers;
using BackendApi.Core.Models;
using NetTopologySuite.Geometries;

namespace BackendApi.Models
{
    /// <summary>
    /// โมเดลร้านค้าสำหรับระบบคำนวณเส้นทางและการปักหมุด
    /// </summary>
    public class Shop : BaseSoftDeleteEntity<string>, ITrackableEntity
    {
        public long RefNumber { get; init; }

        [NotMapped]
        public string TrackingCode => TrackingCodeFormatter.Format(TrackingPrefixes.Shop, RefNumber);
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string MenuName { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal MenuPrice { get; set; }

        /// <summary>
        /// พิกัดเชิงพื้นที่ geometry(Point, 4326) ใน PostGIS สำหรับประมวลผลเชิงระยะทาง
        /// </summary>
        [Column(TypeName = "geometry(Point, 4326)")]
        public Point? Location { get; set; }

        public bool IsOpen { get; set; } = true;

        public int PrepTimeMinutes { get; set; } = 15;

        [MaxLength(100)]
        public string? OpeningHours { get; set; }

        public ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();

        public ICollection<MenuCategory> MenuCategories { get; set; } = new List<MenuCategory>();
    }
}
