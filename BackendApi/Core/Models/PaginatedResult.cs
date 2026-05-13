namespace BackendApi.Core.Models;

/// <summary>
/// ผลลัพธ์แบบแบ่งหน้า สำหรับส่งกลับไปยัง Frontend
/// </summary>
public class PaginatedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}
