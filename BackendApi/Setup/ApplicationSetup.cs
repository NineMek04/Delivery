using BackendApi.Core.Models;
using BackendApi.Hubs;
using Serilog;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Prometheus;
using BackendApi.Infrastructure.EventBus;
using BackendApi.Infrastructure.EventBus.Events;
using BackendApi.Infrastructure.EventBus.Handlers;
using Microsoft.AspNetCore.HttpOverrides;
namespace BackendApi.Setup;

public static class ApplicationSetup
{
    public static WebApplication UseBackendApiPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Delivery Backend API v1");
            });
        }

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                var isServiceUnavailable =
                    exception is Npgsql.NpgsqlException ||
                    exception is TimeoutException ||
                    exception is TaskCanceledException ||
                    exception?.InnerException is TimeoutException ||
                    exception?.InnerException is TaskCanceledException;
                var status = isServiceUnavailable
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status500InternalServerError;
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("GlobalExceptionHandler");

                logger.LogError(
                    exception,
                    "Unhandled pipeline exception. CorrelationId: {CorrelationId}",
                    context.TraceIdentifier);

                context.Response.StatusCode = status;
                await context.Response.WriteAsJsonAsync(ApiResponse.Fail(
                    status,
                    isServiceUnavailable
                        ? "ระบบไม่พร้อมให้บริการชั่วคราว"
                        : "เกิดข้อผิดพลาดภายในเซิร์ฟเวอร์",
                    errorDetail: app.Environment.IsDevelopment() ? exception?.ToString() : null,
                    code: isServiceUnavailable ? "SERVICE_UNAVAILABLE" : "INTERNAL_ERROR"));
            });
        });
        app.UseStatusCodePages(async statusContext =>
        {
            var response = statusContext.HttpContext.Response;
            if (response.HasStarted ||
                response.ContentLength is > 0 ||
                !string.IsNullOrWhiteSpace(response.ContentType))
            {
                return;
            }

            var status = response.StatusCode;
            var (message, code) = status switch
            {
                StatusCodes.Status400BadRequest => ("คำขอไม่ถูกต้อง", "BAD_REQUEST"),
                StatusCodes.Status401Unauthorized => ("กรุณาเข้าสู่ระบบหรือส่ง access token ที่ถูกต้อง", "UNAUTHORIZED"),
                StatusCodes.Status403Forbidden => ("คุณไม่มีสิทธิ์เข้าถึงทรัพยากรนี้", "FORBIDDEN"),
                StatusCodes.Status404NotFound => ("ไม่พบทรัพยากรที่ร้องขอ", "NOT_FOUND"),
                StatusCodes.Status405MethodNotAllowed => ("HTTP method นี้ไม่รองรับสำหรับ endpoint ที่ร้องขอ", "METHOD_NOT_ALLOWED"),
                StatusCodes.Status409Conflict => ("ข้อมูลขัดแย้งกับสถานะปัจจุบัน", "CONFLICT"),
                StatusCodes.Status429TooManyRequests => ("มีคำขอมากเกินไป กรุณาลองใหม่ภายหลัง", "RATE_LIMITED"),
                StatusCodes.Status503ServiceUnavailable => ("ระบบไม่พร้อมให้บริการชั่วคราว", "SERVICE_UNAVAILABLE"),
                _ => ("เกิดข้อผิดพลาดภายในเซิร์ฟเวอร์", "INTERNAL_ERROR")
            };

            await response.WriteAsJsonAsync(ApiResponse.Fail(status, message, code: code));
        });
        app.UseWebSockets();
        app.UseRouting();
        app.UseHttpMetrics(); // Prometheus HTTP metrics
        app.UseCors(ServiceSetup.ClientCorsPolicy);
        app.UseSerilogRequestLogging();

        if (app.Configuration.GetValue("SecurityHeaders:Enabled", true))
        {
            app.UseSecurityHeaders();
        }

        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();

        // Anti-CSRF Middleware (After Authentication so we have User Context)
        app.UseMiddleware<CsrfValidationMiddleware>();

        app.MapMetrics(); // Expose /metrics for Prometheus
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        app.MapHealthChecks("/health/detail", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var response = new
                {
                    status = report.Status.ToString(),
                    results = report.Entries.Select(e => new
                    {
                        check = e.Key,
                        status = e.Value.Status.ToString(),
                        description = e.Value.Description,
                        data = e.Value.Data
                    })
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
            }
        }).RequireAuthorization(BackendApi.Security.AuthConstants.OperationsPolicy);

        app.MapControllers();

        // --- SignalR Hub ---
        app.MapHub<TrackingHub>("/hubs/tracking");
        app.MapHub<ChatHub>("/hubs/chat");

        // --- EventBus Subscriptions (Decoupled background processing) ---
        var eventBus = app.Services.GetRequiredService<IEventBus>();
        eventBus.Subscribe<OrderCreatedIntegrationEvent, OrderCreatedIntegrationEventHandler>();
        eventBus.Subscribe<OrderStatusChangedIntegrationEvent, OrderStatusChangedIntegrationEventHandler>();
        eventBus.Subscribe<RiderLocationUpdatedIntegrationEvent, RiderLocationUpdatedIntegrationEventHandler>();
        eventBus.Subscribe<RiderStateChangedIntegrationEvent, RiderStateChangedIntegrationEventHandler>();

        return app;
    }
}
