using BackendApi.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BackendApi.Core.Filters;

/// <summary>
/// Exception Filter ที่ดักจับ Unhandled Exceptions ทุกตัว
/// แล้วแปลงเป็น ApiResponse รูปแบบมาตรฐาน พร้อม HTTP 500
/// </summary>
public class GlobalExceptionFilter : IExceptionFilter
{
    private readonly ILogger<GlobalExceptionFilter> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger, IHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public void OnException(ExceptionContext context)
    {
        _logger.LogError(context.Exception, "Unhandled exception: {Message}", context.Exception.Message);

        var response = ApiResponse.Fail(
            "เกิดข้อผิดพลาดภายในเซิร์ฟเวอร์",
            errorDetail: _env.IsDevelopment() ? context.Exception.ToString() : null,
            code: "INTERNAL_ERROR"
        );

        context.Result = new ObjectResult(response)
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };

        context.ExceptionHandled = true;
    }
}
