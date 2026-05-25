using BackendApi.Core;
using BackendApi.Core.Constants;
using BackendApi.Core.Mappings;
using BackendApi.Core.Models;
using BackendApi.Models;
using BackendApi.Models.DTOs;
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
                    .ThenInclude(o => o.Items);

            var total = await query.CountAsync(cancellationToken);

            var menuItems = await query
                .OrderBy(m => m.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return Ok(new PaginatedResult<MenuItemDto>
            {
                Items = menuItems.Adapt<List<MenuItemDto>>(),
                TotalCount = total,
                Page = page,
                PageSize = pageSize
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
        /// สร้างเมนูสินค้าใหม่
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<MenuItemDto>> Create(
            [FromBody] CreateMenuItemDto dto,
            CancellationToken cancellationToken = default)
        {
            var entity = dto.Adapt<MenuItem>();

            DB.InsertObject(entity);
            await DB.CommitChangesAsync(cancellationToken);

            var result = entity.Adapt<MenuItemDto>();
            return CreatedAtAction(nameof(GetById), new { id = entity.Id }, result);
        }

        /// <summary>
        /// อัปเดตข้อมูลเมนูสินค้า
        /// </summary>
        [HttpPut("{id}")]
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

            dto.Adapt(entity);

            await DB.CommitChangesAsync(cancellationToken);

            return Ok(entity.Adapt<MenuItemDto>());
        }

        /// <summary>
        /// ลบเมนูสินค้า (Soft Delete)
        /// </summary>
        [HttpDelete("{id}")]
        public override async Task<ActionResult> Delete(string id, CancellationToken cancellationToken = default)
        {
            var deleted = await DB.DeleteObjectAsync<MenuItem>(id, softDelete: true, cancellationToken: cancellationToken);
            if (deleted is null)
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลเมนูสินค้า", code: "NOT_FOUND"));

            await DB.CommitChangesAsync(cancellationToken);

            return NoContent();
        }
    }
}
