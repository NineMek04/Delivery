using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BackendApi.Core.Models;

namespace BackendApi.Models
{
    /// <summary>
    /// โมเดลตารางฐานข้อมูลบน PostgreSQL เก็บข้อความแชทเรียลไทม์จำกัดขอบเขตตามออเดอร์
    /// </summary>
    public class ChatMessage : BaseAuditableEntity<string>
    {
        [Required]
        public string OrderId { get; set; } = string.Empty;

        [ForeignKey(nameof(OrderId))]
        public Order? Order { get; set; }

        [Required]
        public string SenderId { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string SenderRole { get; set; } = string.Empty; // "Rider", "Customer", "Shop"

        [Required]
        [MaxLength(1000)]
        public string Message { get; set; } = string.Empty;
    }
}
