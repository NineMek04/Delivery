using System;
using System.Threading.Tasks;
using BackendApi.Core;
using BackendApi.Core.Models;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Controllers
{
    /// <summary>
    /// API สำหรับการจัดการแจ้งเตือน (Notifications) และลงทะเบียนดีไวซ์ Token
    /// </summary>
    [Authorize]
    [Route("api/v1/[controller]")]
    public class NotificationsController : DeliveryControllerBase
    {
        public NotificationsController()
        {
        }

        /// <summary>
        /// ลงทะเบียนหรืออัปเดตคีย์ FCM Token สำหรับบัญชีผู้ใช้ปัจจุบัน
        /// </summary>
        [HttpPost("register-token")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<ApiResponse>> RegisterToken([FromBody] RegisterFcmTokenDto dto)
        {
            var userId = CurrentUserId;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(ApiResponse.Fail("กรุณาเข้าสู่ระบบก่อนทำรายการ", code: "UNAUTHORIZED"));
            }

            // ค้นหาว่าคีย์ Token นี้เคยลงทะเบียนไว้หรือยัง (เช็คเคสอุปกรณ์เดิมแต่เปลี่ยนยูสเซอร์ใหม่)
            var existingToken = await DB.GetQuery<FcmToken>()
                .FirstOrDefaultAsync(t => t.Token == dto.Token);

            if (existingToken != null)
            {
                // หาก Token มีอยู่แล้วแต่อุปกรณ์ถูกสลับไอดีผู้ใช้ ให้โยกย้ายสิทธิ์การแจ้งเตือน
                existingToken.UserId = userId;
                existingToken.DeviceType = dto.DeviceType ?? existingToken.DeviceType;
                
                DB.UpdateObject(existingToken);
                await DB.CommitChangesAsync();
                
                return Ok(ApiResponse.Ok("อัปเดตอุปกรณ์แจ้งเตือนเรียบร้อยแล้ว"));
            }

            // หากเป็นอุปกรณ์เครื่องใหม่ ให้เพิ่มข้อมูลลงตาราง
            var newToken = new FcmToken
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                Token = dto.Token,
                DeviceType = dto.DeviceType
            };

            DB.InsertObject(newToken);
            await DB.CommitChangesAsync();

            return Ok(ApiResponse.Ok("ลงทะเบียนอุปกรณ์แจ้งเตือนสำเร็จ"));
        }
    }
}
