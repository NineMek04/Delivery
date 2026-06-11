using BackendApi.Data;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Services.BackgroundWorkers;

public class DbMaintenanceWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DbMaintenanceWorker> _logger;

    public DbMaintenanceWorker(IServiceProvider serviceProvider, ILogger<DbMaintenanceWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DbMaintenanceWorker started");

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await PruneProcessedEventsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing database maintenance tasks");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }

        _logger.LogInformation("DbMaintenanceWorker stopped");
    }

    private async Task PruneProcessedEventsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cutoff = DateTime.UtcNow.AddHours(-24);
        var deletedCount = await dbContext.ProcessedEvents
            .Where(pe => pe.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deletedCount > 0)
        {
            _logger.LogInformation("Pruned {Count} stale processed events from database", deletedCount);
        }
    }
}
