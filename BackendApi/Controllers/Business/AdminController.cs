using BackendApi.Core;
using BackendApi.Core.Models;
using BackendApi.Core.StateMachines;
using BackendApi.Models;
using BackendApi.Security;
using BackendApi.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace BackendApi.Controllers.Business
{
    /// <summary>
    /// จัดการการทำงานของ Admin และบันทึก Audit Logs
    /// </summary>
    [Authorize(Roles = AuthConstants.AdminRole)]
    public class AdminController : DeliveryControllerBase
    {
        private readonly IAuditLogger _auditLogger;

        public AdminController(IAuditLogger auditLogger)
        {
            _auditLogger = auditLogger;
        }

        /// <summary>
        /// ระงับสิทธิ์ไรเดอร์ (Suspend Rider)
        /// </summary>
        [HttpPost("rider/{riderId}/suspend")]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ApiResponse>> SuspendRider(string riderId, CancellationToken cancellationToken = default)
        {
            var rider = await DB.GetObjectByKeyAsync<Rider>(riderId, cancellationToken);
            if (rider is null)
            {
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลไรเดอร์", code: "NOT_FOUND"));
            }

            var user = await DB.GetQuery<User>().FirstOrDefaultAsync(u => u.RiderId == riderId, cancellationToken);
            if (user is not null)
            {
                user.IsActive = false;
                user.RefreshToken = null;
                user.RefreshTokenExpiresAt = null;
                DB.UpdateObject(user);
            }

            var beforeState = rider.State.ToString();
            rider.State = RiderState.OFFLINE;
            DB.UpdateObject(rider);
            await DB.CommitChangesAsync(cancellationToken);

            _auditLogger.LogAdminAction(
                action: "SuspendRider",
                targetType: "Rider",
                targetId: riderId,
                beforeState: beforeState,
                afterState: RiderState.OFFLINE.ToString());

            return Ok(ApiResponse.Ok("ระงับสิทธิ์ไรเดอร์สำเร็จ"));
        }

        /// <summary>
        /// ยกเลิกการระงับสิทธิ์ไรเดอร์ (Unsuspend Rider)
        /// </summary>
        [HttpPost("rider/{riderId}/unsuspend")]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ApiResponse>> UnsuspendRider(string riderId, CancellationToken cancellationToken = default)
        {
            var rider = await DB.GetObjectByKeyAsync<Rider>(riderId, cancellationToken);
            if (rider is null)
            {
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลไรเดอร์", code: "NOT_FOUND"));
            }

            var user = await DB.GetQuery<User>().FirstOrDefaultAsync(u => u.RiderId == riderId, cancellationToken);
            if (user is not null)
            {
                user.IsActive = true;
                DB.UpdateObject(user);
            }

            var beforeState = rider.State.ToString();
            // เมื่อปลดระงับ ให้ไรเดอร์กลับมาอยู่ในสถานะ OFFLINE หรือ IDLE ตามสมควร
            // เราให้เริ่มที่ OFFLINE เพื่อให้ไรเดอร์กดเปิดแอป/เชื่อมต่อใหม่เอง
            rider.State = RiderState.OFFLINE;
            DB.UpdateObject(rider);
            await DB.CommitChangesAsync(cancellationToken);

            _auditLogger.LogAdminAction(
                action: "UnsuspendRider",
                targetType: "Rider",
                targetId: riderId,
                beforeState: beforeState,
                afterState: RiderState.OFFLINE.ToString());

            return Ok(ApiResponse.Ok("ยกเลิกการระงับสิทธิ์ไรเดอร์สำเร็จ"));
        }

        /// <summary>
        /// เปลี่ยนบทบาทผู้ใช้ (Change User Role)
        /// </summary>
        [HttpPost("user/{userId}/role")]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ApiResponse>> ChangeUserRole(
            string userId,
            [FromBody] ChangeRoleRequest request,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request?.NewRole))
            {
                return BadRequest(ApiResponse.Fail("กรุณาระบุบทบาทใหม่", code: "INVALID_REQUEST"));
            }

            var allowedRoles = new[]
            {
                AuthConstants.AdminRole,
                AuthConstants.DispatcherRole,
                AuthConstants.RiderRole,
                AuthConstants.CustomerRole,
                AuthConstants.StorePartnerRole
            };
            var normalizedRole = allowedRoles.FirstOrDefault(role =>
                role.Equals(request.NewRole.Trim(), StringComparison.OrdinalIgnoreCase));
            if (normalizedRole is null)
            {
                return BadRequest(ApiResponse.Fail("บทบาทไม่ถูกต้อง", code: "INVALID_ROLE"));
            }

            var user = await DB.GetObjectByKeyAsync<User>(userId, cancellationToken);
            if (user is null)
            {
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลผู้ใช้", code: "NOT_FOUND"));
            }

            var beforeRole = user.Role;
            user.Role = normalizedRole;
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            DB.UpdateObject(user);
            await DB.CommitChangesAsync(cancellationToken);

            _auditLogger.LogAdminAction(
                action: "ChangeUserRole",
                targetType: "User",
                targetId: userId,
                beforeState: beforeRole,
                afterState: normalizedRole);

            return Ok(ApiResponse.Ok("เปลี่ยนบทบาทผู้ใช้สำเร็จ"));
        }

        /// <summary>
        /// ลบผู้ใช้ (Delete User)
        /// </summary>
        [HttpDelete("user/{userId}")]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ApiResponse>> DeleteUser(string userId, CancellationToken cancellationToken = default)
        {
            var user = await DB.GetObjectByKeyAsync<User>(userId, cancellationToken);
            if (user is null)
            {
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลผู้ใช้", code: "NOT_FOUND"));
            }

            var beforeState = user.IsActive ? "ACTIVE" : "INACTIVE";
            await DB.DeleteObjectAsync<User>(userId, softDelete: true, cancellationToken);
            await DB.CommitChangesAsync(cancellationToken);

            _auditLogger.LogAdminAction(
                action: "DeleteUser",
                targetType: "User",
                targetId: userId,
                beforeState: beforeState,
                afterState: "DELETED");

            return Ok(ApiResponse.Ok("ลบผู้ใช้สำเร็จ"));
        }

        /// <summary>
        /// รีเซ็ตรหัสผ่าน (Reset Password)
        /// </summary>
        [HttpPost("user/{userId}/reset-password")]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ApiResponse>> ResetPassword(
            string userId,
            [FromBody] ResetPasswordRequest request,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request?.NewPassword))
            {
                return BadRequest(ApiResponse.Fail("กรุณาระบุรหัสผ่านใหม่", code: "INVALID_REQUEST"));
            }

            var user = await DB.GetObjectByKeyAsync<User>(userId, cancellationToken);
            if (user is null)
            {
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลผู้ใช้", code: "NOT_FOUND"));
            }

            user.PasswordHash = PasswordHasher.HashPassword(request.NewPassword);
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            DB.UpdateObject(user);
            await DB.CommitChangesAsync(cancellationToken);

            _auditLogger.LogAdminAction(
                action: "ResetPassword",
                targetType: "User",
                targetId: userId,
                beforeState: "N/A",
                afterState: "PASSWORD_RESET");

            return Ok(ApiResponse.Ok("รีเซ็ตรหัสผ่านผู้ใช้สำเร็จ"));
        }

        /// <summary>
        /// ปิดใช้งานบัญชี (Disable Account)
        /// </summary>
        [HttpPost("user/{userId}/disable")]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ApiResponse>> DisableAccount(string userId, CancellationToken cancellationToken = default)
        {
            var user = await DB.GetObjectByKeyAsync<User>(userId, cancellationToken);
            if (user is null)
            {
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลผู้ใช้", code: "NOT_FOUND"));
            }

            var beforeState = user.IsActive ? "ENABLED" : "DISABLED";
            user.IsActive = false;
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            DB.UpdateObject(user);
            await DB.CommitChangesAsync(cancellationToken);

            _auditLogger.LogAdminAction(
                action: "DisableAccount",
                targetType: "User",
                targetId: userId,
                beforeState: beforeState,
                afterState: "DISABLED");

            return Ok(ApiResponse.Ok("ปิดใช้งานบัญชีสำเร็จ"));
        }

        /// <summary>
        /// เปิดใช้งานบัญชี (Enable Account)
        /// </summary>
        [HttpPost("user/{userId}/enable")]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ApiResponse>> EnableAccount(string userId, CancellationToken cancellationToken = default)
        {
            var user = await DB.GetObjectByKeyAsync<User>(userId, cancellationToken);
            if (user is null)
            {
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลผู้ใช้", code: "NOT_FOUND"));
            }

            var beforeState = user.IsActive ? "ENABLED" : "DISABLED";
            user.IsActive = true;
            DB.UpdateObject(user);
            await DB.CommitChangesAsync(cancellationToken);

            _auditLogger.LogAdminAction(
                action: "EnableAccount",
                targetType: "User",
                targetId: userId,
                beforeState: beforeState,
                afterState: "ENABLED");

            return Ok(ApiResponse.Ok("เปิดใช้งานบัญชีสำเร็จ"));
        }
    }

    public class ChangeRoleRequest
    {
        public string NewRole { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        [Required]
        [StringLength(128, MinimumLength = 12)]
        public string NewPassword { get; set; } = string.Empty;
    }
}
