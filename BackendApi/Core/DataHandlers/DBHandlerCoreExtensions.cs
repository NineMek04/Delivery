using BackendApi.Core.Models;
using BackendApi.Core.Models.Response;
using BackendApi.Core.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Core.DataHandlers;

/// <summary>
/// Extension Methods สำหรับ DBHandlerCore เพื่อเพิ่มความสามารถ Pagination
/// </summary>
public static class DBHandlerCoreExtensions
{
    /// <summary>
    /// ดึงข้อมูลแบบแบ่งหน้า จาก IQueryable
    /// </summary>
    public static async Task<PaginatedResult<T>> ToPaginatedListAsync<T>(
        this IQueryable<T> query,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200; // ป้องกัน query หนักเกินไป

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedResult<T>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// ดึงข้อมูลแบบแบ่งหน้าจาก DBHandlerCore
    /// </summary>
    public static Task<PaginatedResult<TEntity>> GetPaginatedListAsync<TEntity>(
        this DBHandlerCore db,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        return db.GetQuery<TEntity>(asNoTracking: true)
            .ToPaginatedListAsync(page, pageSize, cancellationToken);
    }
}

