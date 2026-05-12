using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace BackendApi.Models
{
    public class Order
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        public string Status { get; set; } = "PENDING"; // PENDING, ASSIGNED, COMPLETED
        
        // พิกัดร้านค้า (จุดรับของ)
        [Column(TypeName = "geometry(Point, 4326)")]
        public Point? PickupLocation { get; set; }
        
        // พิกัดลูกค้า (จุดส่งของ)
        [Column(TypeName = "geometry(Point, 4326)")]
        public Point? DropoffLocation { get; set; }
        
        public DateTime ExpectedDeliveryTime { get; set; }
        
        public string? AssignedRiderId { get; set; }
    }
}