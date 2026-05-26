using BackendApi.Core;
using BackendApi.Core.Constants;
using BackendApi.Core.Mappings;
using BackendApi.Core.Models;
using BackendApi.Core.DataHandlers;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using BackendApi.Services.Tracking;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Controllers.MasterData
{
    /// <summary>
    /// API จัดการข้อมูลร้านค้า (CRUD และ Spatial Location)
    /// </summary>
    [Authorize]
    public class ShopsController : CrudControllerBase<Shop, ShopDto>
    {
        private readonly ITrackingSearchService _searchService;

        public ShopsController(ITrackingSearchService searchService)
        {
            _searchService = searchService;
        }

        /// <summary>
        /// ดึงรายการร้านค้าทั้งหมด (แบ่งหน้า และรองรับการค้นหา)
        /// </summary>
        [HttpGet]
        public override async Task<ActionResult<PaginatedResult<ShopDto>>> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var query = DB.GetQuery<Shop>(asNoTracking: true);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var parsedRef = _searchService.ParseSearchQuery(search, TrackingPrefixes.Shop);
                if (parsedRef.HasValue)
                {
                    // 1. ถ้าระบุรหัสเป๊ะ ยิงตรงเข้าระบบ Index ทันที (เร็วที่สุด)
                    query = query.Where(s => s.RefNumber == parsedRef.Value);
                }
                else
                {
                    // 2. Fallback: ค้นหาด้วยชื่อร้านค้า หรือชื่อเมนู (Case-insensitive)
                    var term = search.Trim().ToLower();
                    query = query.Where(s => s.Name.ToLower().Contains(term) || s.MenuName.ToLower().Contains(term));
                }
            }

            var result = await query
                .OrderBy(s => s.Name)
                .Include(s => s.MenuItems)
                    .ThenInclude(m => m.Options)
                        .ThenInclude(o => o.Items)
                .ToPaginatedListAsync(page, pageSize, cancellationToken);

            return Ok(new PaginatedResult<ShopDto>
            {
                Items = result.Items.Adapt<List<ShopDto>>(),
                TotalCount = result.TotalCount,
                Page = result.Page,
                PageSize = result.PageSize
            });
        }

        /// <summary>
        /// ดึงข้อมูลร้านค้าเดี่ยว (รองรับทั้ง UUID และ Tracking Code)
        /// </summary>
        [HttpGet("{id}")]
        public override async Task<ActionResult<ShopDto>> GetById(string id, CancellationToken cancellationToken = default)
        {
            var parsedRef = _searchService.ParseSearchQuery(id, TrackingPrefixes.Shop);
            if (parsedRef.HasValue)
            {
                var entity = await DB.GetQuery<Shop>()
                    .Include(s => s.MenuItems)
                        .ThenInclude(m => m.Options)
                            .ThenInclude(o => o.Items)
                    .FirstOrDefaultAsync(s => s.RefNumber == parsedRef.Value, cancellationToken);
                if (entity is null)
                    return NotFound(ApiResponse.Fail("ไม่พบข้อมูลร้านค้า", code: "NOT_FOUND"));
                return Ok(entity.Adapt<ShopDto>());
            }

            return await base.GetById(id, cancellationToken);
        }

        /// <summary>
        /// ซ่อน base Create ที่รับ ShopDto เพื่อป้องกัน Swagger conflict
        /// และป้องกัน "Geometry has Z dimension" จาก auto-mapping
        /// </summary>
        [NonAction]
        public override Task<ActionResult<ShopDto>> Create(
            [FromBody] ShopDto dto,
            CancellationToken cancellationToken = default)
            => base.Create(dto, cancellationToken);

        /// <summary>
        /// สร้างร้านค้าใหม่ — รับ CreateShopDto เพื่อให้ Swagger แสดง schema ที่ถูกต้อง
        /// และใช้ mapping CreateShopDto → Shop ผ่าน CreatePoint() ที่ force 2D
        /// ป้องกัน "Geometry has Z dimension but column does not" ใน PostGIS
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<ShopDto>> CreateShop(
            [FromBody] CreateShopDto dto,
            CancellationToken cancellationToken = default)
        {
            // สร้าง Point ด้วย GeometryFactory โดยตรง (force 2D)
            // ไม่ใช้ dto.Adapt<Shop>() เพราะ Mapster อาจสร้าง Point ที่มี Z dimension
            var factory = NetTopologySuite.NtsGeometryServices.Instance
                .CreateGeometryFactory(srid: 4326);

            var entity = new Shop
            {
                Name = dto.Name,
                MenuName = dto.MenuName,
                MenuPrice = dto.MenuPrice,
                Location = factory.CreatePoint(
                    new NetTopologySuite.Geometries.Coordinate(dto.Lng, dto.Lat)),
                MenuItems = new List<MenuItem>()
            };

            DB.InsertObject(entity);
            await DB.CommitChangesAsync(cancellationToken);

            var result = new ShopDto
            {
                Id = entity.Id,
                Name = entity.Name,
                MenuName = entity.MenuName,
                MenuPrice = entity.MenuPrice,
                Lat = entity.Location?.Y,
                Lng = entity.Location?.X,
                CreatedAt = entity.CreatedAt,
                MenuItems = new List<MenuItemDto>()
            };

            return CreatedAtAction(nameof(GetById), new { id = entity.Id }, result);
        }

        /// <summary>
        /// อัปเดตข้อมูลร้านค้า — รับ ShopDto แต่อัปเดตเฉพาะฟิลด์ที่ได้รับอนุญาต
        /// ป้องกันปัญหา Z dimension และการเขียนทับ Read-Only/Init-Only properties เช่น RefNumber
        /// </summary>
        [HttpPut("{id}")]
        public override async Task<ActionResult<ShopDto>> Update(
            string id,
            [FromBody] ShopDto dto,
            CancellationToken cancellationToken = default)
        {
            var existing = await DB.GetQuery<Shop>()
                .Include(s => s.MenuItems)
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            if (existing is null)
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลร้านค้าที่ต้องการแก้ไข", code: "NOT_FOUND"));

            // อัปเดตเฉพาะฟิลด์ที่ปรับปรุงได้
            existing.Name = dto.Name;
            existing.MenuName = dto.MenuName;
            existing.MenuPrice = dto.MenuPrice;
            existing.IsOpen = dto.IsOpen;
            existing.PrepTimeMinutes = dto.PrepTimeMinutes;
            existing.OpeningHours = dto.OpeningHours;

            if (dto.Lat.HasValue && dto.Lng.HasValue)
            {
                var factory = NetTopologySuite.NtsGeometryServices.Instance
                    .CreateGeometryFactory(srid: 4326);
                existing.Location = factory.CreatePoint(
                    new NetTopologySuite.Geometries.Coordinate(dto.Lng.Value, dto.Lat.Value));
            }

            DB.UpdateObject(existing);
            await DB.CommitChangesAsync(cancellationToken);

            return Ok(existing.Adapt<ShopDto>());
        }
    }
}
