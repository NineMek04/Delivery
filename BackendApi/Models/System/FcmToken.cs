using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BackendApi.Core.Models;
using BackendApi.Core.Models.Response;
using BackendApi.Core.Models.Entities;
using BackendApi.Models.Entities;

namespace BackendApi.Models.SystemModels
{
    /// <summary>
    /// โมเดลเก็บคีย์จดทะเบียนของดีไวซ์ FCM สำหรับผู้ใช้แต่ละราย
    /// </summary>
    public class FcmToken : BaseAuditableEntity<string>
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public User? User { get; set; }

        [Required]
        [MaxLength(500)]
        public string Token { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? DeviceType { get; set; } // "Android", "iOS", "Web"
    }
}



