using System;
using System.ComponentModel.DataAnnotations;

namespace BackendApi.Models.DTOs
{
    /// <summary>
    /// DTO สำหรับแสดงข้อมูลหมวดหมู่เมนู
    /// </summary>
    public class MenuCategoryDto
    {
        public string Id { get; set; } = string.Empty;
        public string TrackingCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int DisplayOrder { get; set; }
        public string ShopId { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
    }

    /// <summary>
    /// DTO สำหรับขอสร้างหมวดหมู่เมนูใหม่
    /// </summary>
    public class CreateMenuCategoryDto
    {
        [Required(ErrorMessage = "กรุณากรอกชื่อหมวดหมู่")]
        [MaxLength(100, ErrorMessage = "ชื่อหมวดหมู่ต้องยาวไม่เกิน 100 ตัวอักษร")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "คำอธิบายต้องยาวไม่เกิน 500 ตัวอักษร")]
        public string? Description { get; set; }

        [Range(0, 1000, ErrorMessage = "ลำดับการแสดงผลต้องอยู่ในช่วง 0 ถึง 1000")]
        public int DisplayOrder { get; set; }

        [Required(ErrorMessage = "กรุณาระบุร้านค้า")]
        public string ShopId { get; set; } = string.Empty;
    }

    /// <summary>
    /// DTO สำหรับอัปเดตหมวดหมู่เมนู
    /// </summary>
    public class UpdateMenuCategoryDto
    {
        [MaxLength(100, ErrorMessage = "ชื่อหมวดหมู่ต้องยาวไม่เกิน 100 ตัวอักษร")]
        public string? Name { get; set; }

        [MaxLength(500, ErrorMessage = "คำอธิบายต้องยาวไม่เกิน 500 ตัวอักษร")]
        public string? Description { get; set; }

        [Range(0, 1000, ErrorMessage = "ลำดับการแสดงผลต้องอยู่ในช่วง 0 ถึง 1000")]
        public int? DisplayOrder { get; set; }
    }
}
