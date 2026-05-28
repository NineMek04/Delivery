using BackendApi.Hubs;
using Serilog;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Prometheus;
using BackendApi.Infrastructure.EventBus;
using BackendApi.Infrastructure.EventBus.Events;
using BackendApi.Infrastructure.EventBus.Handlers;
namespace BackendApi.Setup;

public static class ApplicationSetup
{
    public static WebApplication UseBackendApiPipeline(this WebApplication app)
    {

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
        app.UseWebSockets();
        app.UseRouting();
        app.UseHttpMetrics(); // Prometheus HTTP metrics
        app.UseCors(ServiceSetup.ClientCorsPolicy);
        app.UseSerilogRequestLogging();

        if (app.Configuration.GetValue("SecurityHeaders:Enabled", true))
        {
            app.UseSecurityHeaders();
        }

        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

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
        });

        app.MapControllers();

        // --- SignalR Hub ---
        app.MapHub<TrackingHub>("/hubs/tracking");

        // --- EventBus Subscriptions (Decoupled background processing) ---
        var eventBus = app.Services.GetRequiredService<IEventBus>();
        eventBus.Subscribe<OrderCreatedIntegrationEvent, OrderCreatedIntegrationEventHandler>();
        eventBus.Subscribe<OrderStatusChangedIntegrationEvent, OrderStatusChangedIntegrationEventHandler>();
        eventBus.Subscribe<RiderLocationUpdatedIntegrationEvent, RiderLocationUpdatedIntegrationEventHandler>();

        return app;
    }
}
