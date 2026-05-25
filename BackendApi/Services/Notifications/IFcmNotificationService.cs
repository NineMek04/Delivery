using System.Collections.Generic;
using System.Threading.Tasks;

namespace BackendApi.Services.Notifications
{
    /// <summary>
    /// อินเตอร์เฟสสำหรับบริการส่งข้อความแจ้งเตือน Firebase Cloud Messaging (FCM)
    /// </summary>
    public interface IFcmNotificationService
    {
        /// <summary>
        /// ส่งข้อความแจ้งเตือนไปยังผู้ใช้รายบุคคล (ยิงหาทุกอุปกรณ์ที่บันทึกไว้ของยูสเซอร์)
        /// </summary>
        /// <param name="userId">ไอดีผู้ใช้</param>
        /// <param name="title">หัวข้อแจ้งเตือน</param>
        /// <param name="body">รายละเอียดข้อความ</param>
        /// <param name="data">ข้อมูลเพิ่มเติม (Custom payload payload)</param>
        /// <returns>จำนวนอุปกรณ์ที่ส่งสำเร็จ</returns>
        Task<int> SendNotificationToUserAsync(string userId, string title, string body, Dictionary<string, string>? data = null);

        /// <summary>
        /// ส่งข้อความแจ้งเตือนหาอุปกรณ์เจาะจงผ่าน FCM Token ตรงๆ
        /// </summary>
        /// <param name="token">คีย์อุปกรณ์ FCM Token</param>
        /// <param name="title">หัวข้อแจ้งเตือน</param>
        /// <param name="body">รายละเอียดข้อความ</param>
        /// <param name="data">ข้อมูลเพิ่มเติม</param>
        /// <returns>ผลลัพธ์สำเร็จหรือไม่</returns>
        Task<bool> SendNotificationToTokenAsync(string token, string title, string body, Dictionary<string, string>? data = null);
    }
}
