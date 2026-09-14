using BackendApi.Core;
using BackendApi.Core.Constants;
using BackendApi.Core.Mappings;
using BackendApi.Core.Models;
using BackendApi.Core.Models.Response;
using BackendApi.Core.Models.Entities;
using BackendApi.Core.DataHandlers;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using BackendApi.Security.Models;
using BackendApi.Security.Services;
using BackendApi.Services.Tracking;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Controllers.Shops
{
    /// <summary>
    /// API จัดการข้อมูลเมนูสินค้าของร้านค้า (CRUD และการจัดการตัวเลือก)
    /// </summary>
    [Authorize]
    [Route("api/v1/menu-items")]
    [Route("api/v1/menuitems")]
    public class MenuItemsController : CrudControllerBase<MenuItem, MenuItemDto>
    {
        private readonly ITrackingSearchService? _searchService;

        public MenuItemsController(ITrackingSearchService? searchService = null)
        {
            _searchService = searchService;
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
        /// รองรับทั้ง Shop UUID และ Shop Tracking Code (เช่น SHP-000008)
        /// </summary>
        [HttpGet("shop/{shopId}")]
        public async Task<ActionResult<PaginatedResult<MenuItemDto>>> GetByShop(
            string shopId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var targetShopId = shopId;
            var parsedRef = _searchService?.ParseSearchQuery(shopId, TrackingPrefixes.Shop);
            if (parsedRef.HasValue)
            {
                var resolvedShop = await DB.GetQuery<Shop>(asNoTracking: true)
                    .FirstOrDefaultAsync(s => s.RefNumber == parsedRef.Value, cancellationToken);
                if (resolvedShop != null)
                {
                    targetShopId = resolvedShop.Id;
                }
            }

            var query = DB.GetQuery<MenuItem>()
                .Where(m => m.ShopId == targetShopId)
                .Include(m => m.Options)
                    .ThenInclude(o => o.Items)
                .OrderBy(m => m.Name);

            var result = await query.ToPaginatedListAsync(page, pageSize, cancellationToken);
            var dtoList = result.Items.Adapt<List<MenuItemDto>>();

            // Fallback: หากยังไม่มีข้อมูลในตาราง MenuItems ให้ใช้เมนูหลักประจำร้านจากตาราง Shop
            if (dtoList.Count == 0)
            {
                var shop = await DB.GetQuery<Shop>(asNoTracking: true)
                    .FirstOrDefaultAsync(s => s.Id == targetShopId, cancellationToken);
                if (shop != null && !string.IsNullOrWhiteSpace(shop.MenuName))
                {
                    dtoList.Add(new MenuItemDto
                    {
                        Id = $"primary-{shop.Id}",
                        ShopId = shop.Id,
                        Name = shop.MenuName,
                        Price = shop.MenuPrice,
                        Description = "เมนูหลักประจำร้าน"
                    });

                    return Ok(new PaginatedResult<MenuItemDto>
                    {
                        Items = dtoList,
                        TotalCount = 1,
                        Page = 1,
                        PageSize = pageSize
                    });
                }
            }

            return Ok(new PaginatedResult<MenuItemDto>
            {
                Items = dtoList,
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



