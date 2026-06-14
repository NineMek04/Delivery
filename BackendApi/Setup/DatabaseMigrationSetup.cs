using BackendApi.Data;
using BackendApi.ServiceMigration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

            // Bridge databases that completed the pre-squash migration chain.
            // Fresh databases have no history table and apply the baseline normally.
            await MigrationBaselineCompatibility.EnsureBaselineHistoryAsync(context);

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

            // ── ServiceMigration: Run advanced PostgreSQL schema configurator (Partitioning, Clustering & Views) ──
            Log.Information("⚙️ [ServiceMigration] Running advanced PostgreSQL schema configuration...");
            await PostgresAdvancedConfigurator.ConfigureSchemaAsync(context);
            Log.Information("✅ [ServiceMigration] Schema setup completed.");

            // Seed default data (and mock data if enabled)
            var seedMockData = app.Configuration.GetValue<bool>("SeedMockData", false);
            var seedAdminPassword = app.Configuration["SeedAdminPassword"]
                ?? app.Configuration["SEED_ADMIN_PASSWORD"]
                ?? string.Empty;
            Log.Information("🌱 Seeding database (SeedMockData = {SeedMockData})...", seedMockData);
            await DataSeeder.SeedAsync(context, seedAdminPassword, seedMockData);
            Log.Information("✅ Database seeding process complete.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ An error occurred while migrating the database.");
            throw;
        }
    }
}
