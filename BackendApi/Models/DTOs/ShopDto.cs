using System;
using System.ComponentModel.DataAnnotations;

namespace BackendApi.Models.DTOs
{
    /// <summary>
    /// DTO สำหรับส่งข้อมูลร้านค้าไปแสดงผลบนแผนที่หน้าบ้าน
    /// </summary>
    public class ShopDto
    {
        public string Id { get; set; } = string.Empty;
        public string TrackingCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string MenuName { get; set; } = string.Empty;
        public decimal MenuPrice { get; set; }
        
        /// <summary>พิกัดละติจูด</summary>
        public double? Lat { get; set; }
        
        /// <summary>พิกัดลองจิจูด</summary>
        public double? Lng { get; set; }
        
        public DateTime? CreatedAt { get; set; }
        
        public ICollection<MenuItemDto>? MenuItems { get; set; }
    }

    /// <summary>
    /// DTO สำหรับรับคำขอสร้างร้านค้าใหม่จากหน้าบ้าน
    /// </summary>
    public class CreateShopDto
    {
        [Required(ErrorMessage = "กรุณากรอกชื่อร้านค้า")]
        [MaxLength(100, ErrorMessage = "ชื่อร้านค้าต้องมีความยาวไม่เกิน 100 ตัวอักษร")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "กรุณากรอกชื่อเมนูเด่น")]
        [MaxLength(100, ErrorMessage = "ชื่อเมนูต้องมีความยาวไม่เกิน 100 ตัวอักษร")]
        public string MenuName { get; set; } = string.Empty;

        [Range(0.01, 100000.0, ErrorMessage = "ราคาสินค้าต้องมากกว่า 0 บาท")]
        public decimal MenuPrice { get; set; }

        [Required]
        [Range(-90.0, 90.0, ErrorMessage = "พิกัด Latitude ไม่ถูกต้อง")]
        public double Lat { get; set; }

        [Required]
        [Range(-180.0, 180.0, ErrorMessage = "พิกัด Longitude ไม่ถูกต้อง")]
        public double Lng { get; set; }
    }
}
