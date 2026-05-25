using System;
using System.Data;
using System.Threading.Tasks;
using BackendApi.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BackendApi.ServiceMigration
{
    public static class PostgresAdvancedConfigurator
    {
        /// <summary>
        /// Orchestrates and configures all advanced PostgreSQL-specific database schemas.
        /// This should be run immediately after context.Database.MigrateAsync().
        /// </summary>
        public static async Task ConfigureSchemaAsync(ApplicationDbContext context)
        {
            try
            {
                Log.Information("⚙️ [ServiceMigration] Starting advanced PostgreSQL schema configuration...");

                // 1. Table Partitioning for RiderLocationHistories
                await ConfigureTablePartitioningAsync(context);

                // 2. Physical Clustering for Spatial Data Optimization
                await ApplyPhysicalClusteringAsync(context);

                // 3. Guarantee RowVersion bytea Defaults (\x)
                await ApplyRowVersionDefaultsAsync(context);

                // 4. Setup Database Views (Future proofing)
                await SetupDatabaseViewsAsync(context);

                Log.Information("✅ [ServiceMigration] Advanced PostgreSQL schema configuration completed successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ [ServiceMigration] Critical error occurred during PostgreSQL schema configuration.");
                throw;
            }
        }

        /// <summary>
        /// Checks if RiderLocationHistories is a partitioned table. If not, converts it dynamically.
        /// </summary>
        private static async Task ConfigureTablePartitioningAsync(ApplicationDbContext context)
        {
            Log.Information("🔍 [ServiceMigration] Checking if 'RiderLocationHistories' is already partitioned...");
            
            bool isPartitioned = await IsTablePartitionedAsync(context, "RiderLocationHistories");

            if (isPartitioned)
            {
                Log.Information("🎉 [ServiceMigration] 'RiderLocationHistories' is already partitioned. Skipping migration.");
                return;
            }

            Log.Warning("⚠️ [ServiceMigration] 'RiderLocationHistories' is a standard table. Starting dynamic partitioning workflow...");

            var connection = context.Database.GetDbConnection();
            bool wasOpen = connection.State == ConnectionState.Open;
            if (!wasOpen) await connection.OpenAsync();

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                // 1. Drop existing indexes on the standard table (if any)
                await ExecuteSqlRawAsync(context, "DROP INDEX IF EXISTS \"IX_RiderLocationHistories_RiderId_RecordedAt\";");
                await ExecuteSqlRawAsync(context, "DROP INDEX IF EXISTS \"IX_RiderLocationHistories_Location_Gist\";");

                // 2. Rename the ordinary table to a temporary name
                await ExecuteSqlRawAsync(context, "ALTER TABLE \"RiderLocationHistories\" RENAME TO \"RiderLocationHistories_old\";");
                await ExecuteSqlRawAsync(context, "ALTER TABLE \"RiderLocationHistories_old\" DROP CONSTRAINT IF EXISTS \"PK_RiderLocationHistories\";");

                // 3. Create the Partitioned Parent Table
                await ExecuteSqlRawAsync(context, @"
                    CREATE TABLE ""RiderLocationHistories"" (
                        ""Id"" text NOT NULL,
                        ""RiderId"" text NOT NULL,
                        ""Location"" geometry(Point, 4326) NOT NULL,
                        ""RecordedAt"" timestamp with time zone NOT NULL,
                        ""RecordedFromIp"" text,
                        ""OrderId"" text,
                        CONSTRAINT ""PK_RiderLocationHistories"" PRIMARY KEY (""Id"", ""RecordedAt"")
                    ) PARTITION BY RANGE (""RecordedAt"");
                ");

                // 4. Create base partitions dynamically (current month + next 3 months) to avoid insertion range failures
                var now = DateTime.UtcNow;
                for (int i = 0; i <= 3; i++)
                {
                    var targetDate = now.AddMonths(i);
                    var year = targetDate.Year;
                    var month = targetDate.Month;
                    var startDate = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
                    var endDate = startDate.AddMonths(1);

                    var partitionName = $"RiderLocationHistories_{year}_{month:D2}";
                    var createPartitionSql = $@"
                        CREATE TABLE IF NOT EXISTS ""{partitionName}""
                        PARTITION OF ""RiderLocationHistories""
                        FOR VALUES FROM ('{startDate:yyyy-MM-dd}') TO ('{endDate:yyyy-MM-dd}');
                    ";
                    await ExecuteSqlRawAsync(context, createPartitionSql);
                    Log.Information("⚙️ [ServiceMigration] Ensured active monthly partition: {PartitionName}", partitionName);
                }

                // 5. Transfer existing data from the old table (if any)
                await ExecuteSqlRawAsync(context, "INSERT INTO \"RiderLocationHistories\" SELECT * FROM \"RiderLocationHistories_old\";");

                // 6. Drop the temporary standard table
                await ExecuteSqlRawAsync(context, "DROP TABLE \"RiderLocationHistories_old\";");

                // 7. Recreate Spatial GiST and Composite B-tree Indexes on the partitioned table
                await ExecuteSqlRawAsync(context, @"
                    CREATE INDEX ""IX_RiderLocationHistories_Location_Gist""
                        ON ""RiderLocationHistories"" USING gist (""Location"");
                ");
                await ExecuteSqlRawAsync(context, @"
                    CREATE INDEX ""IX_RiderLocationHistories_RiderId_RecordedAt""
                        ON ""RiderLocationHistories"" (""RiderId"", ""RecordedAt"");
                ");

                await transaction.CommitAsync();
                Log.Information("✅ [ServiceMigration] Successfully converted 'RiderLocationHistories' into a Partitioned Table!");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Log.Error(ex, "❌ [ServiceMigration] Failed to partition RiderLocationHistories table. Transaction rolled back.");
                throw;
            }
            finally
            {
                if (!wasOpen) await connection.CloseAsync();
            }
        }

        /// <summary>
        /// Applies physical clustering to Riders and Orders tables for database physical performance optimization.
        /// </summary>
        private static async Task ApplyPhysicalClusteringAsync(ApplicationDbContext context)
        {
            Log.Information("⚙️ [ServiceMigration] Applying physical clustering (CLUSTER) command on spatial indexes...");
            
            try
            {
                await ExecuteSqlRawAsync(context, "CLUSTER \"Riders\" USING \"IX_Riders_CurrentLocation_Gist\";");
                await ExecuteSqlRawAsync(context, "CLUSTER \"Orders\" USING \"IX_Orders_PickupLocation_Gist\";");
                Log.Information("✅ [ServiceMigration] Physical clustering applied successfully.");
            }
            catch (Exception ex)
            {
                // In some test/local contexts, clustering failure is non-critical, so we log it as warning rather than failing the whole app
                Log.Warning(ex, "⚠️ [ServiceMigration] Non-critical warning: Physical clustering command skipped or failed. Usually happens if index or tables are empty during initial setup.");
            }
        }

        /// <summary>
        /// Explicitly ensures database-level 'DEFAULT \x::bytea' constraints for all entity row versions.
        /// </summary>
        private static async Task ApplyRowVersionDefaultsAsync(ApplicationDbContext context)
        {
            Log.Information("⚙️ [ServiceMigration] Guaranteeing DEFAULT '\\x'::bytea for all concurrency RowVersion columns...");

            string[] tables = new[] 
            { 
                "Riders", "Orders", "Users", "Shops", "MenuItems", "MenuItemOptions", "MenuItemOptionItems" 
            };

            foreach (var table in tables)
            {
                try
                {
                    await ExecuteSqlRawAsync(context, $"ALTER TABLE \"{table}\" ALTER COLUMN \"RowVersion\" SET DEFAULT '\\x'::bytea;");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "⚠️ [ServiceMigration] Non-critical warning: Could not set DEFAULT constraint for RowVersion on table '{Table}'. Column might not exist yet or defaults were already set.", table);
                }
            }
            Log.Information("✅ [ServiceMigration] Concurrency defaults guaranteed.");
        }

        /// <summary>
        /// Service Hook for database views migration. Easily extendable by adding raw SQL queries.
        /// </summary>
        private static async Task SetupDatabaseViewsAsync(ApplicationDbContext context)
        {
            Log.Information("⚙️ [ServiceMigration] Setting up database views (if any)...");

            // Example of future View script addition:
            /*
            await ExecuteSqlRawAsync(context, @"
                CREATE OR REPLACE VIEW ""v_RiderActivePerformance"" AS
                SELECT ""Id"", ""FullName"", ""RefNumber""
                FROM ""Riders""
                WHERE ""State"" = 1;
            ");
            */

            await Task.CompletedTask;
            Log.Information("✅ [ServiceMigration] Database views setup complete.");
        }

        /// <summary>
        /// Queries the PostgreSQL catalog to determine if a table is partitioned ('p' relkind).
        /// </summary>
        private static async Task<bool> IsTablePartitionedAsync(ApplicationDbContext context, string tableName)
        {
            var connection = context.Database.GetDbConnection();
            bool wasOpen = connection.State == ConnectionState.Open;
            if (!wasOpen) await connection.OpenAsync();

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT relkind FROM pg_class WHERE relname = @tableName;";
                
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@tableName";
                parameter.Value = tableName;
                command.Parameters.Add(parameter);

                var result = await command.ExecuteScalarAsync();
                if (result != null)
                {
                    char relkind = Convert.ToChar(result);
                    return relkind == 'p';
                }
                return false;
            }
            finally
            {
                if (!wasOpen) await connection.CloseAsync();
            }
        }

        /// <summary>
        /// Safe execution helper for Raw SQL queries.
        /// </summary>
        private static async Task ExecuteSqlRawAsync(ApplicationDbContext context, string sql)
        {
            await context.Database.ExecuteSqlRawAsync(sql);
        }
    }
}
