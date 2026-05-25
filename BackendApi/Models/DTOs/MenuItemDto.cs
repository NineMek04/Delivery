using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BackendApi.Models.DTOs
{
    /// <summary>
    /// DTO สำหรับแสดงข้อมูลเมนูสินค้า
    /// </summary>
    public class MenuItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string TrackingCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public string? ImageUrl { get; set; }
        public string ShopId { get; set; } = string.Empty;
        public string? MenuCategoryId { get; set; }
        public ICollection<MenuItemOptionDto>? Options { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    /// <summary>
    /// DTO สำหรับรับคำขอสร้างเมนูสินค้าใหม่
    /// </summary>
    public class CreateMenuItemDto
    {
        [Required(ErrorMessage = "กรุณากรอกชื่อเมนู")]
        [MaxLength(200, ErrorMessage = "ชื่อเมนูต้องมีความยาวไม่เกิน 200 ตัวอักษร")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "คำอธิบายต้องมีความยาวไม่เกิน 500 ตัวอักษร")]
        public string? Description { get; set; }

        [Range(0.01, 100000.0, ErrorMessage = "ราคาต้องมากกว่า 0 บาท")]
        public decimal Price { get; set; }

        [MaxLength(500, ErrorMessage = "URL รูปภาพต้องมีความยาวไม่เกิน 500 ตัวอักษร")]
        public string? ImageUrl { get; set; }

        [Required(ErrorMessage = "กรุณาระบุร้านค้า")]
        public string ShopId { get; set; } = string.Empty;

        public string? MenuCategoryId { get; set; }

        public ICollection<CreateMenuItemOptionDto>? Options { get; set; }
    }

    /// <summary>
    /// DTO สำหรับอัปเดตเมนูสินค้า
    /// </summary>
    public class UpdateMenuItemDto
    {
        [MaxLength(200, ErrorMessage = "ชื่อเมนูต้องมีความยาวไม่เกิน 200 ตัวอักษร")]
        public string? Name { get; set; }

        [MaxLength(500, ErrorMessage = "คำอธิบายต้องมีความยาวไม่เกิน 500 ตัวอักษร")]
        public string? Description { get; set; }

        [Range(0.01, 100000.0, ErrorMessage = "ราคาต้องมากกว่า 0 บาท")]
        public decimal? Price { get; set; }

        [MaxLength(500, ErrorMessage = "URL รูปภาพต้องมีความยาวไม่เกิน 500 ตัวอักษร")]
        public string? ImageUrl { get; set; }

        public ICollection<UpdateMenuItemOptionDto>? Options { get; set; }
    }

    /// <summary>
    /// DTO สำหรับแสดงข้อมูลตัวเลือกของเมนู
    /// </summary>
    public class MenuItemOptionDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Required { get; set; }
        public int MaxSelections { get; set; }
        public ICollection<MenuItemOptionItemDto>? Items { get; set; }
    }

    /// <summary>
    /// DTO สำหรับรับคำขอสร้างตัวเลือกของเมนู
    /// </summary>
    public class CreateMenuItemOptionDto
    {
        [Required(ErrorMessage = "กรุณากรอกชื่อตัวเลือก")]
        [MaxLength(100, ErrorMessage = "ชื่อตัวเลือกต้องมีความยาวไม่เกิน 100 ตัวอักษร")]
        public string Name { get; set; } = string.Empty;

        public bool Required { get; set; }

        public int MaxSelections { get; set; }

        public ICollection<CreateMenuItemOptionItemDto>? Items { get; set; }
    }

    /// <summary>
    /// DTO สำหรับอัปเดตตัวเลือกของเมนู
    /// </summary>
    public class UpdateMenuItemOptionDto
    {
        [MaxLength(100, ErrorMessage = "ชื่อตัวเลือกต้องมีความยาวไม่เกิน 100 ตัวอักษร")]
        public string? Name { get; set; }

        public bool? Required { get; set; }

        public int? MaxSelections { get; set; }

        public ICollection<UpdateMenuItemOptionItemDto>? Items { get; set; }
    }

    /// <summary>
    /// DTO สำหรับแสดงข้อมูลรายการตัวเลือกย่อย
    /// </summary>
    public class MenuItemOptionItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    /// <summary>
    /// DTO สำหรับรับคำขอสร้างรายการตัวเลือกย่อย
    /// </summary>
    public class CreateMenuItemOptionItemDto
    {
        [Required(ErrorMessage = "กรุณากรอกชื่อตัวเลือกย่อย")]
        [MaxLength(100, ErrorMessage = "ชื่อตัวเลือกย่อยต้องมีความยาวไม่เกิน 100 ตัวอักษร")]
        public string Name { get; set; } = string.Empty;

        [Range(0.01, 100000.0, ErrorMessage = "ราคาต้องมากกว่า 0 บาท")]
        public decimal Price { get; set; }
    }

    /// <summary>
    /// DTO สำหรับอัปเดตรายการตัวเลือกย่อย
    /// </summary>
    public class UpdateMenuItemOptionItemDto
    {
        [MaxLength(100, ErrorMessage = "ชื่อตัวเลือกย่อยต้องมีความยาวไม่เกิน 100 ตัวอักษร")]
        public string? Name { get; set; }

        [Range(0.01, 100000.0, ErrorMessage = "ราคาต้องมากกว่า 0 บาท")]
        public decimal? Price { get; set; }
    }
}
