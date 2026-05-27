using System;
using System.ComponentModel.DataAnnotations;

namespace BackendApi.Models
{
    /// <summary>
    /// ตารางสำหรับตรวจสอบความซ้ำซ้อนของการประมวลผล Integration Event (Idempotency)
    /// </summary>
    public class ProcessedEvent
    {
        public Guid EventId { get; set; }
        
        [Required]
        [MaxLength(250)]
        public string HandlerName { get; set; } = string.Empty;
        
        public DateTime ProcessedAt { get; set; }
    }
}
