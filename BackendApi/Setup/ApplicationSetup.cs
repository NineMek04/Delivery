using BackendApi.Hubs;
using Serilog;
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

        app.UseRouting();
        app.UseCors(ServiceSetup.ClientCorsPolicy);
        app.UseSerilogRequestLogging();

        if (app.Configuration.GetValue("SecurityHeaders:Enabled", true))
        {
            app.UseSecurityHeaders();
        }

        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        // --- SignalR Hub ---
        app.MapHub<TrackingHub>("/hubs/tracking");

        return app;
    }
}
