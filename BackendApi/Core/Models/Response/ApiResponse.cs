namespace BackendApi.Core.Models.Response;

/// <summary>
/// Standard API Response wrapper — ห่อทุก Response ให้อยู่ในรูปแบบเดียวกัน
/// </summary>
public class ApiResponse
{
    public int Status { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorDetail { get; set; }
    public string? Code { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }

    public static ApiResponse Ok(string? message = null) =>
        new() { Status = StatusCodes.Status200OK, Success = true, Message = message ?? "สำเร็จ" };

    public static ApiResponse Fail(string message, string? errorDetail = null, string? code = null) =>
        Fail(StatusCodes.Status400BadRequest, message, errorDetail, code);

    public static ApiResponse Fail(
        int status,
        string message,
        string? errorDetail = null,
        string? code = null,
        Dictionary<string, string[]>? errors = null) =>
        new()
        {
            Status = status,
            Success = false,
            Message = message,
            ErrorDetail = errorDetail,
            Code = code,
            Errors = errors
        };
}

/// <summary>
/// Standard API Response wrapper with payload
/// </summary>
public class ApiResponse<T> : ApiResponse
{
    public T? Value { get; set; }

    public static ApiResponse<T> Ok(T value, string? message = null) =>
        new()
        {
            Status = StatusCodes.Status200OK,
            Success = true,
            Value = value,
            Message = message ?? "สำเร็จ"
        };

    public new static ApiResponse<T> Fail(string message, string? errorDetail = null, string? code = null) =>
        Fail(StatusCodes.Status400BadRequest, message, errorDetail, code);

    public new static ApiResponse<T> Fail(
        int status,
        string message,
        string? errorDetail = null,
        string? code = null,
        Dictionary<string, string[]>? errors = null) =>
        new()
        {
            Status = status,
            Success = false,
            Message = message,
            ErrorDetail = errorDetail,
            Code = code,
            Errors = errors
        };
}

