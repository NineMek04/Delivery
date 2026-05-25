using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BackendApi.Core.Constants;
using BackendApi.Core.Helpers;
using BackendApi.Core.Models;
using NetTopologySuite.Geometries;

namespace BackendApi.Models
{
    /// <summary>
    /// โมเดลข้อมูลที่อยู่จัดส่งของลูกค้า (พร้อมพิกัดเชิงพื้นที่ PostGIS)
    /// </summary>
    public class CustomerAddress : BaseSoftDeleteEntity<string>, ITrackableEntity
    {
        public long RefNumber { get; init; }

        [NotMapped]
        public string TrackingCode => TrackingCodeFormatter.Format(TrackingPrefixes.CustomerAddress, RefNumber);

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public User? User { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty; // เช่น "บ้าน", "คอนโด", "ที่ทำงาน"

        [Required]
        [MaxLength(200)]
        public string AddressLine1 { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? AddressLine2 { get; set; }

        [Required]
        [MaxLength(100)]
        public string City { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string State { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string PostalCode { get; set; } = string.Empty;

        /// <summary>
        /// พิกัดเชิงพื้นที่ geometry(Point, 4326) ใน PostGIS
        /// </summary>
        [Required]
        [Column(TypeName = "geometry(Point, 4326)")]
        public Point? Location { get; set; }

        public bool IsDefault { get; set; }
    }
}
