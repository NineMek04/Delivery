using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace BackendApi.Models
{
    public class Rider
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string Name { get; set; } = string.Empty;
        
        public string Status { get; set; } = "AVAILABLE"; // AVAILABLE, DELIVERING, OFFLINE
        
        // ฟิลด์สำคัญ: ใช้เก็บพิกัด GPS สำหรับให้ PostGIS คำนวณระยะทาง
        [Column(TypeName = "geometry(Point, 4326)")]
        public Point? CurrentLocation { get; set; } 
        
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}