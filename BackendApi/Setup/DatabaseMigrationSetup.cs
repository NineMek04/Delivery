using BackendApi.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BackendApi.Setup;

public static class DatabaseMigrationSetup
{
    /// <summary>
    /// Automatically applies any pending EF Core migrations to the database.
    /// This should be called before app.Run().
    /// </summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        try
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            
            // Check if there are any pending migrations
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
            var migrations = pendingMigrations.ToList();
            
            if (migrations.Any())
            {
                Log.Information("🚀 Found {Count} pending migrations: {Migrations}", migrations.Count, string.Join(", ", migrations));
                Log.Information("⏳ Applying migrations to the database...");
                
                await context.Database.MigrateAsync();
                
                Log.Information("✅ Database migrations applied successfully.");
            }
            else
            {
                Log.Information("Healthy: Database is up to date. No pending migrations.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ An error occurred while migrating the database.");
            // In a production environment, you might want to stop the application
            // if migrations fail, depending on your deployment strategy.
        }
    }
}
