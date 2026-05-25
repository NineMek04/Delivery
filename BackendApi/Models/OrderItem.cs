using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BackendApi.Core.Models;

namespace BackendApi.Models
{
    /// <summary>
    /// โมเดลรายการสินค้าภายในออเดอร์ (เก็บสแนปช็อตราคาและตัวเลือกพิเศษ ณ ขณะชำระเงินจริง)
    /// </summary>
    public class OrderItem : BaseSoftDeleteEntity<string>
    {
        [Required]
        public string OrderId { get; set; } = string.Empty;

        [ForeignKey(nameof(OrderId))]
        public Order? Order { get; set; }

        [Required]
        public string MenuItemId { get; set; } = string.Empty;

        [ForeignKey(nameof(MenuItemId))]
        public MenuItem? MenuItem { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty; // ชื่อสินค้า ณ ขณะสั่งซื้อ

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; } // ราคาสินค้าต่อหน่วย ณ ขณะสั่งซื้อ

        public int Quantity { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; } // หมายเหตุเพิ่มเติม (เช่น เผ็ดน้อย, ไม่ใส่ผัก)

        [MaxLength(500)]
        public string? OptionsDescription { get; set; } // ตัวเลือกสแนปช็อตย่อย (ขนาด, ท็อปปิ้ง)

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalPrice => UnitPrice * Quantity;
    }
}
