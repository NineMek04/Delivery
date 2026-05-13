namespace BackendApi.Core.Models;

/// <summary>
/// Standard API Response wrapper — ห่อทุก Response ให้อยู่ในรูปแบบเดียวกัน
/// </summary>
public class ApiResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorDetail { get; set; }
    public string? Code { get; set; }

    public static ApiResponse Ok(string? message = null) =>
        new() { Success = true, Message = message ?? "สำเร็จ" };

    public static ApiResponse Fail(string message, string? errorDetail = null, string? code = null) =>
        new() { Success = false, Message = message, ErrorDetail = errorDetail, Code = code };
}

/// <summary>
/// Standard API Response wrapper with payload
/// </summary>
public class ApiResponse<T> : ApiResponse
{
    public T? Value { get; set; }

    public static ApiResponse<T> Ok(T value, string? message = null) =>
        new() { Success = true, Value = value, Message = message ?? "สำเร็จ" };

    public new static ApiResponse<T> Fail(string message, string? errorDetail = null, string? code = null) =>
        new() { Success = false, Message = message, ErrorDetail = errorDetail, Code = code };
}
