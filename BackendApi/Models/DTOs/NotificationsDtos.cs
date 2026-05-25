using System.ComponentModel.DataAnnotations;

namespace BackendApi.Models.DTOs
{
    /// <summary>
    /// DTO สำหรับการลงทะเบียน FCM Token จากดีไวซ์ของผู้ใช้
    /// </summary>
    public class RegisterFcmTokenDto
    {
        [Required(ErrorMessage = "กรุณาระบุ FCM Token")]
        public string Token { get; set; } = string.Empty;

        public string? DeviceType { get; set; } // "Android", "iOS", "Web"
    }
}
