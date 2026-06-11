using BackendApi.Core;
using BackendApi.Core.Constants;
using BackendApi.Core.Models;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BackendApi.Controllers.MasterData
{
    /// <summary>
    /// API จัดการข้อมูลหมวดหมู่สินค้าของร้านค้า (CRUD หมวดหมู่)
    /// </summary>
    [Authorize]
    public class MenuCategoriesController : CrudControllerBase<MenuCategory, MenuCategoryDto>
    {
        public MenuCategoriesController()
        {
        }

        /// <summary>
        /// ดึงรายการหมวดหมู่สินค้าทั้งหมดของร้านค้าเดี่ยว (เรียงตามลำดับแสดงผล)
        /// </summary>
        [HttpGet("shop/{shopId}")]
        public async Task<ActionResult<ApiResponse<List<MenuCategoryDto>>>> GetByShop(
            string shopId,
            CancellationToken cancellationToken = default)
        {
            var categories = await DB.GetQuery<MenuCategory>()
                .Where(c => c.ShopId == shopId)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Name)
                .ToListAsync(cancellationToken);

            return Ok(ApiResponse<List<MenuCategoryDto>>.Ok(categories.Adapt<List<MenuCategoryDto>>()));
        }

        /// <summary>
        /// ซ่อน base Create เพื่อหลีกเลี่ยงข้อขัดแย้ง Swagger
        /// </summary>
        [NonAction]
        public override Task<ActionResult<MenuCategoryDto>> Create(
            [FromBody] MenuCategoryDto dto,
            CancellationToken cancellationToken = default)
            => base.Create(dto, cancellationToken);

        /// <summary>
        /// ซ่อน base Update เพื่อหลีกเลี่ยงข้อขัดแย้ง Swagger
        /// </summary>
        [NonAction]
        public override Task<ActionResult<MenuCategoryDto>> Update(
            string id,
            [FromBody] MenuCategoryDto dto,
            CancellationToken cancellationToken = default)
            => base.Update(id, dto, cancellationToken);

        /// <summary>
        /// สร้างหมวดหมู่สินค้าใหม่
        /// </summary>
        [HttpPost]
        [Authorize(Roles = $"{AuthConstants.AdminRole},{AuthConstants.StorePartnerRole}")]
        public async Task<ActionResult<MenuCategoryDto>> CreateCategory(
            [FromBody] CreateMenuCategoryDto dto,
            CancellationToken cancellationToken = default)
        {
            if (!CanManageShop(dto.ShopId))
                return Forbid();

            var entity = dto.Adapt<MenuCategory>();

            DB.InsertObject(entity);
            await DB.CommitChangesAsync(cancellationToken);

            var result = entity.Adapt<MenuCategoryDto>();
            return CreatedAtAction(nameof(GetById), new { id = entity.Id }, result);
        }

        /// <summary>
        /// อัปเดตข้อมูลหมวดหมู่สินค้า
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = $"{AuthConstants.AdminRole},{AuthConstants.StorePartnerRole}")]
        public async Task<ActionResult<MenuCategoryDto>> UpdateCategory(
            string id,
            [FromBody] UpdateMenuCategoryDto dto,
            CancellationToken cancellationToken = default)
        {
            var entity = await DB.GetObjectByKeyAsync<MenuCategory>(id, cancellationToken);

            if (entity is null)
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลหมวดหมู่สินค้า", code: "NOT_FOUND"));

            if (!CanManageShop(entity.ShopId))
                return Forbid();

            dto.Adapt(entity);
            DB.UpdateObject(entity);
            await DB.CommitChangesAsync(cancellationToken);

            return Ok(entity.Adapt<MenuCategoryDto>());
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = $"{AuthConstants.AdminRole},{AuthConstants.StorePartnerRole}")]
        public override async Task<ActionResult> Delete(
            string id,
            CancellationToken cancellationToken = default)
        {
            var entity = await DB.GetObjectByKeyAsync<MenuCategory>(id, cancellationToken);
            if (entity is null)
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลหมวดหมู่สินค้า", code: "NOT_FOUND"));

            if (!CanManageShop(entity.ShopId))
                return Forbid();

            await DB.DeleteObjectAsync<MenuCategory>(
                id,
                softDelete: true,
                cancellationToken: cancellationToken);
            await DB.CommitChangesAsync(cancellationToken);
            return NoContent();
        }

        private bool CanManageShop(string shopId) =>
            User.IsInRole(AuthConstants.AdminRole) ||
            string.Equals(
                User.FindFirst("shop_id")?.Value,
                shopId,
                StringComparison.Ordinal);
    }
}
