using BackendApi.Core.DataHandlers;
using BackendApi.Core.Models;
using Mapster;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Core;

/// <summary>
/// Base Controller สำหรับ Master Data (CRUD พื้นฐาน)
/// ใช้กับตารางข้อมูลที่ไม่มี Business Logic ซับซ้อน เช่น ประเภทรถ, สถานะ, สาขา
/// สำหรับ Entity ที่มีลอจิกซับซ้อน (เช่น Order, Dispatch) ให้แยก Controller เขียนเอง
/// </summary>
/// <typeparam name="TEntity">Entity class ที่ map กับ DB</typeparam>
/// <typeparam name="TDto">DTO class สำหรับส่งกลับไปยัง Frontend</typeparam>
public abstract class CrudControllerBase<TEntity, TDto> : DeliveryControllerBase
    where TEntity : class
    where TDto : class
{
    /// <summary>
    /// ดึงข้อมูลทั้งหมด (แบบแบ่งหน้า)
    /// </summary>
    /// <param name="page">หมายเลขหน้า (เริ่มจาก 1)</param>
    /// <param name="pageSize">จำนวนรายการต่อหน้า (สูงสุด 200)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ผลลัพธ์แบบแบ่งหน้า</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public virtual async Task<ActionResult<PaginatedResult<TDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await DB.GetPaginatedListAsync<TEntity>(page, pageSize, cancellationToken);

        return Ok(new PaginatedResult<TDto>
        {
            Items = result.Items.Adapt<List<TDto>>(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        });
    }

    /// <summary>
    /// ดึงข้อมูลตาม Primary Key
    /// </summary>
    /// <param name="id">Primary Key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ข้อมูลรายการเดียว</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public virtual async Task<ActionResult<TDto>> GetById(string id, CancellationToken cancellationToken = default)
    {
        var entity = await DB.GetObjectByKeyAsync<TEntity>(id, cancellationToken);

        if (entity is null)
            return NotFound(ApiResponse.Fail("ไม่พบข้อมูลที่ร้องขอ", code: "NOT_FOUND"));

        return Ok(entity.Adapt<TDto>());
    }

    /// <summary>
    /// สร้างข้อมูลใหม่
    /// </summary>
    /// <param name="dto">ข้อมูลที่ต้องการสร้าง</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ข้อมูลที่ถูกสร้าง</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public virtual async Task<ActionResult<TDto>> Create(
        [FromBody] TDto dto,
        CancellationToken cancellationToken = default)
    {
        var entity = dto.Adapt<TEntity>();
        DB.InsertObject(entity);
        await DB.CommitChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById),
            new { id = GetEntityId(entity) },
            entity.Adapt<TDto>());
    }

    /// <summary>
    /// อัปเดตข้อมูล
    /// </summary>
    /// <param name="id">Primary Key ของข้อมูลที่ต้องการแก้ไข</param>
    /// <param name="dto">ข้อมูลที่ต้องการอัปเดต</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ข้อมูลที่ถูกแก้ไข</returns>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public virtual async Task<ActionResult<TDto>> Update(
        string id,
        [FromBody] TDto dto,
        CancellationToken cancellationToken = default)
    {
        var existing = await DB.GetObjectByKeyAsync<TEntity>(id, cancellationToken);

        if (existing is null)
            return NotFound(ApiResponse.Fail("ไม่พบข้อมูลที่ต้องการแก้ไข", code: "NOT_FOUND"));

        dto.Adapt(existing);
        DB.UpdateObject(existing);
        await DB.CommitChangesAsync(cancellationToken);

        return Ok(existing.Adapt<TDto>());
    }

    /// <summary>
    /// ลบข้อมูล (Soft Delete)
    /// </summary>
    /// <param name="id">Primary Key ของข้อมูลที่ต้องการลบ</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ผลลัพธ์การลบ</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public virtual async Task<ActionResult> Delete(string id, CancellationToken cancellationToken = default)
    {
        var deleted = await DB.DeleteObjectAsync<TEntity>(id, softDelete: true, cancellationToken: cancellationToken);

        if (deleted is null)
            return NotFound(ApiResponse.Fail("ไม่พบข้อมูลที่ต้องการลบ", code: "NOT_FOUND"));

        await DB.CommitChangesAsync(cancellationToken);
        return Ok(ApiResponse.Ok("ลบข้อมูลสำเร็จ"));
    }

    /// <summary>
    /// ดึง Primary Key จาก Entity — สามารถ override ได้ถ้า key ไม่ใช่ property ชื่อ "Id"
    /// </summary>
    protected virtual string GetEntityId(TEntity entity)
    {
        var idProp = typeof(TEntity).GetProperty("Id");
        return idProp?.GetValue(entity)?.ToString() ?? string.Empty;
    }
}
