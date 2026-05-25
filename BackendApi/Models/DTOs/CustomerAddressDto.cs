using System;
using System.ComponentModel.DataAnnotations;

namespace BackendApi.Models.DTOs
{
    /// <summary>
    /// DTO สำหรับแสดงข้อมูลที่อยู่จัดส่งของลูกค้า
    /// </summary>
    public class CustomerAddressDto
    {
        public string Id { get; set; } = string.Empty;
        public string TrackingCode { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AddressLine1 { get; set; } = string.Empty;
        public string? AddressLine2 { get; set; }
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        
        public bool IsDefault { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    /// <summary>
    /// DTO สำหรับส่งคำขอสร้างที่อยู่จัดส่งใหม่
    /// </summary>
    public class CreateCustomerAddressDto
    {
        [Required(ErrorMessage = "กรุณากรอกชื่อเรียกที่อยู่ (เช่น บ้าน, ที่ทำงาน)")]
        [MaxLength(100, ErrorMessage = "ชื่อเรียกต้องมีความยาวไม่เกิน 100 ตัวอักษร")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "กรุณากรอกที่อยู่บรรทัดที่ 1")]
        [MaxLength(200, ErrorMessage = "ที่อยู่ต้องมีความยาวไม่เกิน 200 ตัวอักษร")]
        public string AddressLine1 { get; set; } = string.Empty;

        [MaxLength(200, ErrorMessage = "รายละเอียดต้องมีความยาวไม่เกิน 200 ตัวอักษร")]
        public string? AddressLine2 { get; set; }

        [Required(ErrorMessage = "กรุณากรอกจังหวัด/เมือง")]
        [MaxLength(100, ErrorMessage = "ชื่อจังหวัด/เมืองต้องยาวไม่เกิน 100 ตัวอักษร")]
        public string City { get; set; } = string.Empty;

        [Required(ErrorMessage = "กรุณากรอกอำเภอ/เขต")]
        [MaxLength(100, ErrorMessage = "ชื่ออำเภอ/เขตต้องยาวไม่เกิน 100 ตัวอักษร")]
        public string State { get; set; } = string.Empty;

        [Required(ErrorMessage = "กรุณากรอกรหัสไปรษณีย์")]
        [MaxLength(20, ErrorMessage = "รหัสไปรษณีย์ต้องมีความยาวไม่เกิน 20 ตัวอักษร")]
        public string PostalCode { get; set; } = string.Empty;

        [Required]
        [Range(-90.0, 90.0, ErrorMessage = "พิกัด Latitude ไม่ถูกต้อง")]
        public double Latitude { get; set; }

        [Required]
        [Range(-180.0, 180.0, ErrorMessage = "พิกัด Longitude ไม่ถูกต้อง")]
        public double Longitude { get; set; }

        public bool IsDefault { get; set; }
    }

    /// <summary>
    /// DTO สำหรับขอแก้ไขข้อมูลที่อยู่จัดส่ง
    /// </summary>
    public class UpdateCustomerAddressDto
    {
        [MaxLength(100, ErrorMessage = "ชื่อเรียกต้องมีความยาวไม่เกิน 100 ตัวอักษร")]
        public string? Name { get; set; }

        [MaxLength(200, ErrorMessage = "ที่อยู่ต้องมีความยาวไม่เกิน 200 ตัวอักษร")]
        public string? AddressLine1 { get; set; }

        [MaxLength(200, ErrorMessage = "รายละเอียดต้องมีความยาวไม่เกิน 200 ตัวอักษร")]
        public string? AddressLine2 { get; set; }

        [MaxLength(100, ErrorMessage = "ชื่อจังหวัด/เมืองต้องยาวไม่เกิน 100 ตัวอักษร")]
        public string? City { get; set; }

        [MaxLength(100, ErrorMessage = "ชื่ออำเภอ/เขตต้องยาวไม่เกิน 100 ตัวอักษร")]
        public string? State { get; set; }

        [MaxLength(20, ErrorMessage = "รหัสไปรษณีย์ต้องมีความยาวไม่เกิน 20 ตัวอักษร")]
        public string? PostalCode { get; set; }

        [Range(-90.0, 90.0, ErrorMessage = "พิกัด Latitude ไม่ถูกต้อง")]
        public double? Latitude { get; set; }

        [Range(-180.0, 180.0, ErrorMessage = "พิกัด Longitude ไม่ถูกต้อง")]
        public double? Longitude { get; set; }

        public bool? IsDefault { get; set; }
    }
}
