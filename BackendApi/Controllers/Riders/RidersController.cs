using BackendApi.Core;
using BackendApi.Core.Constants;
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

namespace BackendApi.Controllers.Riders;

/// <summary>
/// จัดการข้อมูล Rider (ไรเดอร์/พนักงานขับรถ)
/// </summary>
[Authorize(Policy = AuthConstants.OperationsPolicy)]
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
        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            existing.Name = dto.Name;
        }

        if (!string.IsNullOrEmpty(dto.Status) && Enum.TryParse<BackendApi.Core.StateMachines.RiderState>(dto.Status, true, out var parsedState))
        {
            existing.State = parsedState;
        }

        DB.UpdateObject(existing);
        await DB.CommitChangesAsync(cancellationToken);

        return Ok(existing.Adapt<RiderDto>());
    }

    /// <summary>
    /// ดึงรายการออเดอร์ที่ COMPLETED ของ Rider ในช่วงเวลาที่กำหนด
    /// ใช้สำหรับหน้า History ใน Admin Dashboard เพื่อแสดง list งานย้อนหลังก่อนดู GPS path
    /// </summary>
    /// <param name="riderId">UUID ของ Rider</param>
    /// <param name="fromUtc">เวลาเริ่มต้น (UTC) — default คือ 00:00 ของวันปัจจุบัน (UTC)</param>
    /// <param name="toUtc">เวลาสิ้นสุด (UTC) — default คือเวลาปัจจุบัน</param>
    /// <param name="limit">จำนวนสูงสุดที่ดึง (1-200, default 100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet("{riderId}/completed-orders")]
    public async Task<ActionResult<ApiResponse<List<RiderCompletedOrderDto>>>> GetCompletedOrders(
        string riderId,
        [FromQuery(Name = "from")] DateTime? fromUtc = null,
        [FromQuery(Name = "to")]   DateTime? toUtc   = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        // ── 1. ตรวจ Rider มีอยู่จริง ───────────────────────────────────
        var riderExists = await DB.GetQuery<Rider>(asNoTracking: true)
            .AnyAsync(r => r.Id == riderId, cancellationToken);

        if (!riderExists)
            return NotFound(ApiResponse<List<RiderCompletedOrderDto>>.Fail(
                "ไม่พบข้อมูลไรเดอร์", code: "NOT_FOUND"));

        // ── 2. Normalize time range ────────────────────────────────────
        var to   = NormalizeUtc(toUtc   ?? DateTime.UtcNow);
        var from = NormalizeUtc(fromUtc ?? DateTime.UtcNow.Date); // ถ้าไม่ส่ง from → 00:00 UTC วันนี้

        if (from >= to)
            return BadRequest(ApiResponse<List<RiderCompletedOrderDto>>.Fail(
                "'from' ต้องน้อยกว่า 'to'", code: "INVALID_TIME_RANGE"));

        if (to - from > TimeSpan.FromDays(31))
            return BadRequest(ApiResponse<List<RiderCompletedOrderDto>>.Fail(
                "ช่วงเวลาไม่เกิน 31 วัน", code: "TIME_RANGE_TOO_LARGE"));

        limit = Math.Clamp(limit, 1, 200);

        // ── 3. Query orders + Shop (ไม่ N+1) ──────────────────────────
        var orders = await DB.GetQuery<Order>(asNoTracking: true)
            .Include(o => o.Shop)
            .Where(o =>
                o.AssignedRiderId == riderId &&
                o.State == BackendApi.Core.StateMachines.OrderState.COMPLETED &&
                o.CompletedAt >= from &&
                o.CompletedAt <= to)
            .OrderByDescending(o => o.CompletedAt)
            .Take(limit)
            .Select(o => new RiderCompletedOrderDto
            {
                Id            = o.Id,
                TrackingCode  = o.TrackingCode,
                ShopName      = o.Shop != null ? o.Shop.Name : null,
                DeliveryAddress = o.DeliveryAddress,
                DeliveryFee   = o.DeliveryFee,
                DistanceKm    = o.DistanceKm,
                PickupLat     = o.PickupLocation != null ? o.PickupLocation.Y : null,
                PickupLng     = o.PickupLocation != null ? o.PickupLocation.X : null,
                DropoffLat    = o.DropoffLocation != null ? o.DropoffLocation.Y : null,
                DropoffLng    = o.DropoffLocation != null ? o.DropoffLocation.X : null,
                AssignedAt    = o.AssignedAt,
                CompletedAt   = o.CompletedAt,
                CreatedAt     = o.CreatedAt,
                Rating        = o.Rating
            })
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<List<RiderCompletedOrderDto>>.Ok(orders));
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc   => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _                  => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

}



