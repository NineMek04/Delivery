namespace BackendApi.Core.Models.Response;

/// <summary>
/// ผลลัพธ์จาก Service layer — ใช้ส่งข้อมูลกลับจาก Service ไปยัง Controller
/// 
/// ต่างจาก ApiResponse ตรงที่:
/// - ApiResponse → รูปแบบ JSON ที่ส่งกลับ Client (Response body)
/// - ServiceResult → ผลลัพธ์ภายใน Service → Controller (มี StatusCode เพื่อให้ Controller ตัดสินใจ HTTP status)
/// 
/// ใช้ร่วมกันทุก Service ไม่ต้องสร้างซ้ำ (เช่น AuthService, OrderService, DispatchService)
/// </summary>
/// <typeparam name="T">ประเภทข้อมูลที่ส่งกลับ</typeparam>
public sealed class ServiceResult<T>
{
    public bool Succeeded { get; private init; }
    public int StatusCode { get; private init; }
    public string? Message { get; private init; }
    public string? Code { get; private init; }
    public T? Value { get; private init; }
    public int? RetryAfterSeconds { get; private init; }
    public string? LockedUntil { get; private init; }

    /// <summary>
    /// สร้างผลลัพธ์สำเร็จ
    /// </summary>
    public static ServiceResult<T> Success(
        T value,
        string? message = null,
        int statusCode = 200) =>
        new()
        {
            Succeeded = true,
            StatusCode = statusCode,
            Message = message,
            Value = value
        };

    /// <summary>
    /// สร้างผลลัพธ์ล้มเหลว
    /// </summary>
    public static ServiceResult<T> Failure(
        int statusCode,
        string message,
        string? code = null) =>
        new()
        {
            Succeeded = false,
            StatusCode = statusCode,
            Message = message,
            Code = code
        };

    /// <summary>
    /// สร้างผลลัพธ์ล้มเหลวเนื่องจากบัญชีถูกล็อก
    /// </summary>
    public static ServiceResult<T> FailureWithLockout(
        int statusCode,
        string message,
        string code,
        int retryAfterSeconds,
        string lockedUntil) =>
        new()
        {
            Succeeded = false,
            StatusCode = statusCode,
            Message = message,
            Code = code,
            RetryAfterSeconds = retryAfterSeconds,
            LockedUntil = lockedUntil
        };

    /// <summary>
    /// แปลง ServiceResult เป็น ApiResponse สำหรับส่งกลับ Client
    /// </summary>
    public ApiResponse<T> ToApiResponse() =>
        Succeeded && Value is not null
            ? ApiResponse<T>.Ok(Value, Message)
            : ApiResponse<T>.Fail(Message ?? "เกิดข้อผิดพลาด", code: Code);

    /// <summary>
    /// แปลง ServiceResult (ไม่มี Value) เป็น ApiResponse สำหรับ error case
    /// </summary>
    public ApiResponse ToApiResponseBase() =>
        Succeeded
            ? ApiResponse.Ok(Message)
            : ApiResponse.Fail(Message ?? "เกิดข้อผิดพลาด", code: Code);
}

