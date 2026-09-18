using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using SixLabors.ImageSharp;

namespace BackendApi.Core.Attributes
{
    /// <summary>
    /// ตรวจสอบความถูกต้องของรูปภาพ Base64:
    /// 1. รองรับทั้ง Data URL (data:image/...;base64,...) และ Raw Base64 รวมถึง URL (http/https)
    /// 2. ตรวจสอบขนาดไม่เกิน MaxSizeBytes (ค่าเริ่มต้น 2MB)
    /// 3. ตรวจสอบ Magic Bytes (JPEG, PNG, WebP)
    /// 4. ตรวจสอบโครงสร้างไฟล์ภาพด้วย ImageSharp
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class Base64ImageAttribute : ValidationAttribute
    {
        public int MaxSizeBytes { get; set; } = 2 * 1024 * 1024; // 2 MB Default

        public Base64ImageAttribute()
        {
            ErrorMessage = "ข้อมูลรูปภาพไม่ถูกต้อง";
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value == null)
            {
                return ValidationResult.Success;
            }

            if (value is not string stringValue || string.IsNullOrWhiteSpace(stringValue))
            {
                return ValidationResult.Success;
            }

            var trimmed = stringValue.Trim();

            // อนุญาตถ้าเป็น HTTP / HTTPS URL เดิมอยู่แล้ว
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Success;
            }

            // แยก Data URL prefix (data:image/...;base64,) ออกถ้ามี
            string base64Data = trimmed;
            int commaIndex = trimmed.IndexOf(',');
            if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
            {
                base64Data = trimmed.Substring(commaIndex + 1).Trim();
            }

            // ตรวจสอบความยาวเบื้องต้นก่อน decode เพื่อป้องกัน DoS
            // Base64 1 byte = 4/3 chars -> 2MB ~ 2.8MB base64 string
            long estimatedMaxChars = (long)Math.Ceiling(MaxSizeBytes * 4.0 / 3.0) + 1024;
            if (base64Data.Length > estimatedMaxChars)
            {
                int maxMb = MaxSizeBytes / (1024 * 1024);
                return new ValidationResult($"ขนาดไฟล์รูปภาพเกินกำหนด (สูงสุด {maxMb}MB)");
            }

            // Decode Base64
            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(base64Data);
            }
            catch (FormatException)
            {
                return new ValidationResult("รูปแบบสตริง Base64 ไม่ถูกต้อง");
            }

            if (imageBytes.Length > MaxSizeBytes)
            {
                int maxMb = MaxSizeBytes / (1024 * 1024);
                return new ValidationResult($"ขนาดไฟล์รูปภาพเกินกำหนด (สูงสุด {maxMb}MB)");
            }

            // ตรวจสอบ Magic Bytes
            if (!IsValidImageMagicBytes(imageBytes))
            {
                return new ValidationResult("รูปแบบไฟล์รูปภาพไม่รองรับ ต้องเป็น JPEG, PNG หรือ WebP เท่านั้น");
            }

            // ตรวจสอบความสมบูรณ์ของโครงสร้างรูปภาพด้วย ImageSharp
            try
            {
                using var stream = new MemoryStream(imageBytes);
                var imageInfo = Image.Identify(stream);
                if (imageInfo == null || imageInfo.Width <= 0 || imageInfo.Height <= 0)
                {
                    return new ValidationResult("โครงสร้างไฟล์รูปภาพเสียหายหรือไม่ถูกต้อง");
                }
            }
            catch (Exception)
            {
                return new ValidationResult("ไม่สามารถประมวลผลไฟล์รูปภาพได้ โครงสร้างไฟล์อาจเสียหาย");
            }

            return ValidationResult.Success;
        }

        private static bool IsValidImageMagicBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12)
            {
                return false;
            }

            // JPEG: FF D8 FF
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return true;
            }

            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (bytes.Length >= 8 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            {
                return true;
            }

            // WebP: RIFF....WEBP (52 49 46 46 .... 57 45 42 50)
            if (bytes.Length >= 12 &&
                bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            {
                return true;
            }

            return false;
        }
    }
}
