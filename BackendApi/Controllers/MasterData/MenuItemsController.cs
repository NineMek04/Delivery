using BackendApi.Core;
using BackendApi.Core.Constants;
using BackendApi.Core.Mappings;
using BackendApi.Core.Models;
using BackendApi.Core.DataHandlers;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Controllers.MasterData
{
    /// <summary>
    /// API จัดการข้อมูลเมนูสินค้าของร้านค้า (CRUD และการจัดการตัวเลือก)
    /// </summary>
    [Authorize]
    public class MenuItemsController : CrudControllerBase<MenuItem, MenuItemDto>
    {
        public MenuItemsController()
        {
        }

        /// <summary>
        /// ดึงรายการเมนูสินค้าทั้งหมด (กรองเฉพาะร้านที่เปิดอยู่)
        /// </summary>
        [HttpGet]
        public override async Task<ActionResult<PaginatedResult<MenuItemDto>>> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var query = DB.GetQuery<MenuItem>()
                .Include(m => m.Shop)
                .Where(m => m.Shop != null && m.Shop.IsOpen);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(m => m.Name.ToLower().Contains(term) || (m.Description != null && m.Description.ToLower().Contains(term)));
            }

            var result = await query
                .OrderBy(m => m.Name)
                .Include(m => m.Options)
                    .ThenInclude(o => o.Items)
                .ToPaginatedListAsync(page, pageSize, cancellationToken);

            return Ok(new PaginatedResult<MenuItemDto>
            {
                Items = result.Items.Adapt<List<MenuItemDto>>(),
                TotalCount = result.TotalCount,
                Page = result.Page,
                PageSize = result.PageSize
            });
        }

        /// <summary>
        /// ดึงรายการเมนูสินค้าทั้งหมดของร้านค้าหนึ่งร้าน (แบ่งหน้า)
        /// </summary>
        [HttpGet("shop/{shopId}")]
        public async Task<ActionResult<PaginatedResult<MenuItemDto>>> GetByShop(
            string shopId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var query = DB.GetQuery<MenuItem>()
                .Where(m => m.ShopId == shopId)
                .Include(m => m.Options)
                    .ThenInclude(o => o.Items)
                .OrderBy(m => m.Name);

            var result = await query.ToPaginatedListAsync(page, pageSize, cancellationToken);

            return Ok(new PaginatedResult<MenuItemDto>
            {
                Items = result.Items.Adapt<List<MenuItemDto>>(),
                TotalCount = result.TotalCount,
                Page = result.Page,
                PageSize = result.PageSize
            });
        }

        /// <summary>
        /// ดึงข้อมูลเมนูสินค้าเดี่ยวพร้อมตัวเลือกทั้งหมด
        /// </summary>
        [HttpGet("{id}")]
        public override async Task<ActionResult<MenuItemDto>> GetById(string id, CancellationToken cancellationToken = default)
        {
            var entity = await DB.GetQuery<MenuItem>()
                .Include(m => m.Options)
                    .ThenInclude(o => o.Items)
                .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

            if (entity is null)
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลเมนูสินค้า", code: "NOT_FOUND"));

            return Ok(entity.Adapt<MenuItemDto>());
        }

        /// <summary>
        /// ซ่อน base Create ที่รับ MenuItemDto เพื่อป้องกัน Swagger conflict
        /// </summary>
        [NonAction]
        public override Task<ActionResult<MenuItemDto>> Create(
            [FromBody] MenuItemDto dto,
            CancellationToken cancellationToken = default)
            => base.Create(dto, cancellationToken);

        /// <summary>
        /// ซ่อน base Update ที่รับ MenuItemDto เพื่อป้องกัน Swagger conflict
        /// </summary>
        [NonAction]
        public override Task<ActionResult<MenuItemDto>> Update(
            string id,
            [FromBody] MenuItemDto dto,
            CancellationToken cancellationToken = default)
            => base.Update(id, dto, cancellationToken);

        /// <summary>
        /// สร้างเมนูสินค้าใหม่
        /// </summary>
        [HttpPost]
        [Authorize(Roles = $"{AuthConstants.AdminRole},{AuthConstants.StorePartnerRole}")]
        public async Task<ActionResult<MenuItemDto>> Create(
            [FromBody] CreateMenuItemDto dto,
            CancellationToken cancellationToken = default)
        {
            if (!CanManageShop(dto.ShopId))
                return Forbid();

            if (!string.IsNullOrWhiteSpace(dto.MenuCategoryId))
            {
                var categoryBelongsToShop = await DB.GetQuery<MenuCategory>(asNoTracking: true)
                    .AnyAsync(
                        category => category.Id == dto.MenuCategoryId &&
                                    category.ShopId == dto.ShopId,
                        cancellationToken);
                if (!categoryBelongsToShop)
                    return BadRequest(ApiResponse.Fail("หมวดหมู่เมนูไม่ได้อยู่ในร้านค้าที่ระบุ", code: "INVALID_CATEGORY"));
            }

            var entity = dto.Adapt<MenuItem>();
            if (string.IsNullOrWhiteSpace(entity.MenuCategoryId))
            {
                entity.MenuCategoryId = null;
            }

            DB.InsertObject(entity);
            await DB.CommitChangesAsync(cancellationToken);

            var result = entity.Adapt<MenuItemDto>();
            return CreatedAtAction(nameof(GetById), new { id = entity.Id }, result);
        }

        /// <summary>
        /// อัปเดตข้อมูลเมนูสินค้า
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = $"{AuthConstants.AdminRole},{AuthConstants.StorePartnerRole}")]
        public async Task<ActionResult<MenuItemDto>> Update(
            string id,
            [FromBody] UpdateMenuItemDto dto,
            CancellationToken cancellationToken = default)
        {
            var entity = await DB.GetQuery<MenuItem>()
                .Include(m => m.Options)
                .ThenInclude(o => o.Items)
                .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

            if (entity is null)
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลเมนูสินค้า", code: "NOT_FOUND"));

            if (!CanManageShop(entity.ShopId))
                return Forbid();

            if (!string.IsNullOrWhiteSpace(dto.MenuCategoryId))
            {
                var categoryBelongsToShop = await DB.GetQuery<MenuCategory>(asNoTracking: true)
                    .AnyAsync(
                        category => category.Id == dto.MenuCategoryId &&
                                    category.ShopId == entity.ShopId,
                        cancellationToken);
                if (!categoryBelongsToShop)
                    return BadRequest(ApiResponse.Fail("หมวดหมู่เมนูไม่ได้อยู่ในร้านค้าที่ระบุ", code: "INVALID_CATEGORY"));
            }

            dto.Adapt(entity);
            
            // Map the category ID explicitly if it is provided, in case Mapster IgnoreNulls behaves differently.
            if (dto.MenuCategoryId != null)
            {
                entity.MenuCategoryId = string.IsNullOrWhiteSpace(dto.MenuCategoryId) ? null : dto.MenuCategoryId;
            }

            await DB.CommitChangesAsync(cancellationToken);

            return Ok(entity.Adapt<MenuItemDto>());
        }

        /// <summary>
        /// ลบเมนูสินค้า (Soft Delete)
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = $"{AuthConstants.AdminRole},{AuthConstants.StorePartnerRole}")]
        public override async Task<ActionResult> Delete(string id, CancellationToken cancellationToken = default)
        {
            var entity = await DB.GetObjectByKeyAsync<MenuItem>(id, cancellationToken);
            if (entity is null)
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลเมนูสินค้า", code: "NOT_FOUND"));

            if (!CanManageShop(entity.ShopId))
                return Forbid();

            var deleted = await DB.DeleteObjectAsync<MenuItem>(id, softDelete: true, cancellationToken: cancellationToken);
            if (deleted is null)
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลเมนูสินค้า", code: "NOT_FOUND"));

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
