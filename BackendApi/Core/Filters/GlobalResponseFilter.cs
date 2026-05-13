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
            // ถ้าเป็น ApiResponse อยู่แล้ว ไม่ต้องห่อซ้ำ
            if (objectResult.Value is ApiResponse)
            {
                await next();
                return;
            }

            // ถ้าเป็น ProblemDetails (validation error จาก framework) ไม่ต้องห่อ
            if (objectResult.Value is ProblemDetails)
            {
                await next();
                return;
            }

            var statusCode = objectResult.StatusCode ?? 200;

            if (statusCode >= 200 && statusCode < 300)
            {
                // Success → wrap ด้วย ApiResponse
                var wrapped = new ApiResponse<object>
                {
                    Success = true,
                    Message = "สำเร็จ",
                    Value = objectResult.Value
                };

                objectResult.Value = wrapped;
                objectResult.StatusCode = statusCode;
            }
        }

        await next();
    }
}
