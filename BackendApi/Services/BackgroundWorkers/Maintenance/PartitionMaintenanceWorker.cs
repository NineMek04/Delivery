using BackendApi.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Services.BackgroundWorkers.Maintenance;
using BackendApi.Services.BackgroundWorkers.Queues;

public class PartitionMaintenanceWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PartitionMaintenanceWorker> _logger;

    public PartitionMaintenanceWorker(IServiceProvider serviceProvider, ILogger<PartitionMaintenanceWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PartitionMaintenanceWorker started");

        // First run immediately on startup
        await CreatePartitionsSafeAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        var lastRunDate = DateTime.UtcNow.Date;

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var now = DateTime.UtcNow;
                    if (now.Hour >= 2 && now.Date > lastRunDate)
                    {
                        await CreatePartitionsSafeAsync(stoppingToken);
                        lastRunDate = now.Date;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in PartitionMaintenanceWorker execution loop");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }

        _logger.LogInformation("PartitionMaintenanceWorker stopped");
    }

    private async Task CreatePartitionsSafeAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var now = DateTime.UtcNow;
            // สร้าง Partition ล่วงหน้า 3 เดือน (เดือนปัจจุบัน + 2 เดือนถัดไป)
            for (int i = 0; i <= 2; i++)
            {
                var targetDate = now.AddMonths(i);
                var yearStr = targetDate.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture);
                var monthStr = targetDate.ToString("MM", System.Globalization.CultureInfo.InvariantCulture);
                
                var year = int.Parse(yearStr);
                var month = int.Parse(monthStr);
                var startDate = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
                var endDate = startDate.AddMonths(1);

                var partitionName = $"RiderLocationHistories_{yearStr}_{monthStr}";
                var startStr = startDate.ToString("yyyy-MM-dd HH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
                var endStr = endDate.ToString("yyyy-MM-dd HH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
                var sql = $@"
                    CREATE TABLE IF NOT EXISTS ""{partitionName}""
                    PARTITION OF ""RiderLocationHistories""
                    FOR VALUES FROM ('{startStr}') TO ('{endStr}');
                ";

                try
                {
                    await dbContext.Database.ExecuteSqlRawAsync(sql, ct);
                    _logger.LogInformation("Ensured partition exists: {PartitionName}", partitionName);
                }
                catch (PostgresException pgEx) when (
                    pgEx.SqlState == "42809" || // wrong_object_type — table is not partitioned
                    pgEx.SqlState == "42P01")   // undefined_table — parent table does not exist
                {
                    // Parent table ยังไม่ถูก Partition (Migration ยังไม่รัน) — ข้ามทั้งหมด
                    _logger.LogWarning(
                        "Skipping partition creation — parent table is not partitioned yet (SqlState={SqlState}). " +
                        "Run 'dotnet ef database update' to apply Phase3EnterpriseSpatialScaling migration first.",
                        pgEx.SqlState);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error creating partition {PartitionName}", partitionName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreatePartitionsSafeAsync");
        }
    }
}


