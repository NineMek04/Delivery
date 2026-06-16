using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using BackendApi.Core.Models;
using BackendApi.Core.Models.Response;
using BackendApi.Core.Models.Entities;

namespace BackendApi.Core.Filters;

/// <summary>
/// Action Filter ที่ตรวจสอบ Validation โดยอัตโนมัติจาก DI-registered validators
/// ดักก่อนเข้า Action Method — ถ้าไม่ผ่านจะส่ง HTTP 400 พร้อม ApiResponse กลับทันที
/// </summary>
public class ValidationFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _serviceProvider;

    public ValidationFilter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;

            var argumentType = argument.GetType();
            var validatorType = typeof(IValidator<>).MakeGenericType(argumentType);
            var validator = _serviceProvider.GetService(validatorType);

            if (validator is null) continue;

            var validateMethod = validatorType.GetMethod("ValidateAsync",
                [argumentType, typeof(CancellationToken)]);

            if (validateMethod is null) continue;

            var resultTask = (Task<ValidationResult>)validateMethod.Invoke(
                validator, [argument, CancellationToken.None])!;

            var result = await resultTask;

            if (!result.IsValid)
            {
                var errors = result.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray()
                    );

                var response = ApiResponse.Fail(
                    StatusCodes.Status400BadRequest,
                    "ข้อมูลไม่ผ่านการตรวจสอบ",
                    errorDetail: string.Join("; ", result.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")),
                    code: "VALIDATION_ERROR",
                    errors: errors);

                context.Result = new BadRequestObjectResult(response);
                return;
            }
        }

        await next();
    }
}

