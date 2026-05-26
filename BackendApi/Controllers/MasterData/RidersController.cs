using BackendApi.Core;
using BackendApi.Core.Constants;
using BackendApi.Core.Models;
using BackendApi.Core.DataHandlers;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using BackendApi.Services.Tracking;
using Mapster;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Controllers.MasterData;

/// <summary>
/// จัดการข้อมูล Rider (ไรเดอร์/พนักงานขับรถ)
/// </summary>
public class RidersController : CrudControllerBase<Rider, RiderDto>
{
    private readonly ITrackingSearchService _searchService;

    public RidersController(ITrackingSearchService searchService)
    {
        _searchService = searchService;
    }

    /// <summary>
    /// ดึงข้อมูลทั้งหมด (แบบแบ่งหน้า และรองรับการค้นหา)
    /// </summary>
    [HttpGet]
    public override async Task<ActionResult<PaginatedResult<RiderDto>>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = DB.GetQuery<Rider>(asNoTracking: true);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var parsedRef = _searchService.ParseSearchQuery(search, TrackingPrefixes.Rider);
            if (parsedRef.HasValue)
            {
                // 1. ถ้าระบุรหัสเป๊ะ ยิงตรงเข้าระบบ Index ทันที (เร็วที่สุด)
                query = query.Where(r => r.RefNumber == parsedRef.Value);
            }
            else
            {
                // 2. Fallback: ค้นหาด้วยชื่อไรเดอร์ (Case-insensitive)
                var term = search.Trim().ToLower();
                query = query.Where(r => r.Name.ToLower().Contains(term));
            }
        }

        var result = await query
            .OrderBy(r => r.Name)
            .ToPaginatedListAsync(page, pageSize, cancellationToken);

        return Ok(new PaginatedResult<RiderDto>
        {
            Items = result.Items.Adapt<List<RiderDto>>(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        });
    }

    /// <summary>
    /// ดึงข้อมูลไรเดอร์เดี่ยว (รองรับทั้ง UUID และ Tracking Code)
    /// </summary>
    [HttpGet("{id}")]
    public override async Task<ActionResult<RiderDto>> GetById(string id, CancellationToken cancellationToken = default)
    {
        var parsedRef = _searchService.ParseSearchQuery(id, TrackingPrefixes.Rider);
        if (parsedRef.HasValue)
        {
            var entity = await DB.GetQuery<Rider>().FirstOrDefaultAsync(r => r.RefNumber == parsedRef.Value, cancellationToken);
            if (entity is null)
                return NotFound(ApiResponse.Fail("ไม่พบข้อมูลไรเดอร์", code: "NOT_FOUND"));
            return Ok(entity.Adapt<RiderDto>());
        }

        return await base.GetById(id, cancellationToken);
    }

    /// <summary>
    /// อัปเดตข้อมูลไรเดอร์ — อัปเดตเฉพาะฟิลด์ที่แก้ไขได้ เช่น ชื่อ
    /// ป้องกันปัญหาในการเขียนทับ Read-Only/Init-Only properties เช่น RefNumber
    /// </summary>
    [HttpPut("{id}")]
    public override async Task<ActionResult<RiderDto>> Update(
        string id,
        [FromBody] RiderDto dto,
        CancellationToken cancellationToken = default)
    {
        var existing = await DB.GetObjectByKeyAsync<Rider>(id, cancellationToken);

        if (existing is null)
            return NotFound(ApiResponse.Fail("ไม่พบข้อมูลไรเดอร์ที่ต้องการแก้ไข", code: "NOT_FOUND"));

        // อัปเดตเฉพาะฟิลด์ที่อนุญาต
        existing.Name = dto.Name;
        
        if (!string.IsNullOrEmpty(dto.Status) && Enum.TryParse<BackendApi.Core.StateMachines.RiderState>(dto.Status, true, out var parsedState))
        {
            existing.State = parsedState;
        }

        DB.UpdateObject(existing);
        await DB.CommitChangesAsync(cancellationToken);

        return Ok(existing.Adapt<RiderDto>());
    }
}
