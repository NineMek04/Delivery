using BackendApi.Core;
using BackendApi.Core.Mappings;
using BackendApi.Core.Models;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BackendApi.Controllers.MasterData
{
    /// <summary>
    /// API จัดการที่อยู่จัดส่งของลูกค้า (CRUD และที่อยู่เริ่มต้น)
    /// </summary>
    [Authorize]
    public class CustomerAddressesController : CrudControllerBase<CustomerAddress, CustomerAddressDto>
    {
        public CustomerAddressesController()
        {
        }

        /// <summary>
        /// ดึงรายการที่อยู่จัดส่งทั้งหมดของผู้ใช้ปัจจุบัน (เรียงตามที่อยู่เริ่มต้นก่อน)
        /// </summary>
        [HttpGet]
        public override async Task<ActionResult<PaginatedResult<CustomerAddressDto>>> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var userId = CurrentUserId;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(ApiResponse.Fail("กรุณาเข้าสู่ระบบ", code: "UNAUTHORIZED"));
            }

            var query = DB.GetQuery<CustomerAddress>(asNoTracking: true)
                .Where(a => a.UserId == userId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(a => a.Name.ToLower().Contains(term) || a.AddressLine1.ToLower().Contains(term));
            }

            var total = await query.CountAsync(cancellationToken);

            var addresses = await query
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return Ok(new PaginatedResult<CustomerAddressDto>
            {
                Items = addresses.Adapt<List<CustomerAddressDto>>(),
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            });
        }

        /// <summary>
        /// ดึงข้อมูลที่อยู่เดี่ยวตาม ID (จำกัดสิทธิ์เฉพาะเจ้าของ)
        /// </summary>
        [HttpGet("{id}")]
        public override async Task<ActionResult<CustomerAddressDto>> GetById(string id, CancellationToken cancellationToken = default)
        {
            var userId = CurrentUserId;
            var entity = await DB.GetObjectByKeyAsync<CustomerAddress>(id, cancellationToken);

            if (entity is null)
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลที่อยู่", code: "NOT_FOUND"));

            if (entity.UserId != userId)
                return Forbid();

            return Ok(entity.Adapt<CustomerAddressDto>());
        }

        /// <summary>
        /// ซ่อน base Create เพื่อป้องกัน Swagger conflict
        /// </summary>
        [NonAction]
        public override Task<ActionResult<CustomerAddressDto>> Create(
            [FromBody] CustomerAddressDto dto,
            CancellationToken cancellationToken = default)
            => base.Create(dto, cancellationToken);

        /// <summary>
        /// ซ่อน base Update เพื่อป้องกัน Swagger conflict
        /// </summary>
        [NonAction]
        public override Task<ActionResult<CustomerAddressDto>> Update(
            string id,
            [FromBody] CustomerAddressDto dto,
            CancellationToken cancellationToken = default)
            => base.Update(id, dto, cancellationToken);

        /// <summary>
        /// สร้างข้อมูลที่อยู่จัดส่งใหม่สำหรับลูกค้าปัจจุบัน
        /// </summary>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<CustomerAddressDto>> CreateAddress(
            [FromBody] CreateCustomerAddressDto dto,
            CancellationToken cancellationToken = default)
            {
            var userId = CurrentUserId;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(ApiResponse.Fail("กรุณาเข้าสู่ระบบก่อนทำรายการ", code: "UNAUTHORIZED"));
            }

            // จัดการธุรกรรมในการล้างค่า IsDefault ของที่อยู่อื่น หากตั้งค่าที่อยู่ใหม่เป็นที่อยู่เริ่มต้น
            if (dto.IsDefault)
            {
                await ResetOtherDefaultAddressesAsync(userId, cancellationToken);
            }

            var entity = new CustomerAddress
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                Name = dto.Name,
                AddressLine1 = dto.AddressLine1,
                AddressLine2 = dto.AddressLine2,
                City = dto.City,
                State = dto.State,
                PostalCode = dto.PostalCode,
                Location = MappingConfig.CreatePoint(dto.Longitude, dto.Latitude),
                IsDefault = dto.IsDefault
            };

            DB.InsertObject(entity);
            await DB.CommitChangesAsync(cancellationToken);

            var result = entity.Adapt<CustomerAddressDto>();
            return CreatedAtAction(nameof(GetById), new { id = entity.Id }, result);
        }

        /// <summary>
        /// แก้ไขข้อมูลที่อยู่จัดส่ง
        /// </summary>
        [HttpPut("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<CustomerAddressDto>> UpdateAddress(
            string id,
            [FromBody] UpdateCustomerAddressDto dto,
            CancellationToken cancellationToken = default)
        {
            var userId = CurrentUserId;
            var entity = await DB.GetObjectByKeyAsync<CustomerAddress>(id, cancellationToken);

            if (entity is null)
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลที่อยู่จัดส่ง", code: "NOT_FOUND"));

            if (entity.UserId != userId)
                return Forbid();

            // หากมีการเปลี่ยนให้ที่อยู่นี้เป็นที่อยู่เริ่มต้น ให้ล้างค่าที่อยู่อื่นๆ
            if (dto.IsDefault == true && !entity.IsDefault)
            {
                await ResetOtherDefaultAddressesAsync(userId, cancellationToken);
            }

            // อัปเดตข้อมูลพิกัดหากมีการป้อนข้อมูลพิกัดใหม่เข้ามา
            if (dto.Latitude.HasValue && dto.Longitude.HasValue)
            {
                entity.Location = MappingConfig.CreatePoint(dto.Longitude.Value, dto.Latitude.Value);
            }

            dto.Adapt(entity);
            DB.UpdateObject(entity);
            await DB.CommitChangesAsync(cancellationToken);

            return Ok(entity.Adapt<CustomerAddressDto>());
        }

        /// <summary>
        /// ลบที่อยู่จัดส่ง (Soft Delete)
        /// </summary>
        [HttpDelete("{id}")]
        public override async Task<ActionResult> Delete(string id, CancellationToken cancellationToken = default)
        {
            var userId = CurrentUserId;
            var entity = await DB.GetObjectByKeyAsync<CustomerAddress>(id, cancellationToken);

            if (entity is null)
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลที่อยู่จัดส่ง", code: "NOT_FOUND"));

            if (entity.UserId != userId)
                return Forbid();

            await DB.DeleteObjectAsync<CustomerAddress>(id, softDelete: true, cancellationToken: cancellationToken);
            await DB.CommitChangesAsync(cancellationToken);

            return Ok(ApiResponse.Ok("ลบที่อยู่จัดส่งสำเร็จ"));
        }

        private async Task ResetOtherDefaultAddressesAsync(string userId, CancellationToken cancellationToken)
        {
            var defaultAddresses = await DB.GetQuery<CustomerAddress>()
                .Where(a => a.UserId == userId && a.IsDefault)
                .ToListAsync(cancellationToken);

            foreach (var addr in defaultAddresses)
            {
                addr.IsDefault = false;
                DB.UpdateObject(addr);
            }
        }
    }
}
