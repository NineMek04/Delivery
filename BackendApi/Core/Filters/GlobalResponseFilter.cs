using BackendApi.Core.Attributes;
using BackendApi.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BackendApi.Core.Filters;

/// <summary>
/// Action Filter ที่ห่อทุก ObjectResult ให้อยู่ในรูปแบบ ApiResponse อัตโนมัติ
/// สามารถ Bypass ด้วย [DisableWrapper] attribute
/// </summary>
public class GlobalResponseFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        // ตรวจสอบว่ามี [DisableWrapper] attribute ไหม
        var hasDisableWrapper =
            context.ActionDescriptor.EndpointMetadata.Any(m => m is DisableWrapperAttribute);

        if (hasDisableWrapper)
        {
            await next();
            return;
        }

        if (context.Result is ObjectResult objectResult)
        {
            var statusCode = objectResult.StatusCode ?? StatusCodes.Status200OK;

            // ถ้าเป็น ApiResponse อยู่แล้ว ไม่ต้องห่อซ้ำ
            if (objectResult.Value is ApiResponse apiResponse)
            {
                apiResponse.Status = statusCode;
                if (statusCode >= StatusCodes.Status400BadRequest)
                {
                    apiResponse.Message ??= GetDefaultMessage(statusCode);
                    apiResponse.Code ??= GetDefaultCode(statusCode);
                }
                await next();
                return;
            }

            if (objectResult.Value is ProblemDetails problemDetails)
            {
                objectResult.Value = ApiResponse.Fail(
                    statusCode,
                    problemDetails.Detail ?? problemDetails.Title ?? "คำขอไม่ถูกต้อง",
                    code: statusCode == StatusCodes.Status400BadRequest
                        ? "VALIDATION_ERROR"
                        : "HTTP_ERROR");
                objectResult.StatusCode = statusCode;
                await next();
                return;
            }

            if (statusCode >= 200 && statusCode < 300)
            {
                // Success → wrap ด้วย ApiResponse
                var wrapped = new ApiResponse<object>
                {
                    Status = statusCode,
                    Success = true,
                    Message = "สำเร็จ",
                    Value = objectResult.Value
                };

                objectResult.Value = wrapped;
                objectResult.StatusCode = statusCode;
            }
            else
            {
                objectResult.Value = ApiResponse.Fail(
                    statusCode,
                    GetDefaultMessage(statusCode),
                    code: GetDefaultCode(statusCode));
                objectResult.StatusCode = statusCode;
            }
        }
        else if (context.Result is StatusCodeResult statusCodeResult &&
                 statusCodeResult.StatusCode >= StatusCodes.Status400BadRequest)
        {
            context.Result = new ObjectResult(ApiResponse.Fail(
                statusCodeResult.StatusCode,
                GetDefaultMessage(statusCodeResult.StatusCode),
                code: GetDefaultCode(statusCodeResult.StatusCode)))
            {
                StatusCode = statusCodeResult.StatusCode
            };
        }

        await next();
    }

    private static string GetDefaultMessage(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "คำขอไม่ถูกต้อง",
        StatusCodes.Status401Unauthorized => "กรุณาเข้าสู่ระบบหรือส่ง access token ที่ถูกต้อง",
        StatusCodes.Status403Forbidden => "คุณไม่มีสิทธิ์เข้าถึงทรัพยากรนี้",
        StatusCodes.Status404NotFound => "ไม่พบทรัพยากรที่ร้องขอ",
        StatusCodes.Status409Conflict => "ข้อมูลขัดแย้งกับสถานะปัจจุบัน",
        StatusCodes.Status429TooManyRequests => "มีคำขอมากเกินไป กรุณาลองใหม่ภายหลัง",
        StatusCodes.Status503ServiceUnavailable => "ระบบไม่พร้อมให้บริการชั่วคราว",
        _ => "เกิดข้อผิดพลาดภายในเซิร์ฟเวอร์"
    };

    private static string GetDefaultCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "BAD_REQUEST",
        StatusCodes.Status401Unauthorized => "UNAUTHORIZED",
        StatusCodes.Status403Forbidden => "FORBIDDEN",
        StatusCodes.Status404NotFound => "NOT_FOUND",
        StatusCodes.Status409Conflict => "CONFLICT",
        StatusCodes.Status429TooManyRequests => "RATE_LIMITED",
        StatusCodes.Status503ServiceUnavailable => "SERVICE_UNAVAILABLE",
        _ => "INTERNAL_ERROR"
    };
}
