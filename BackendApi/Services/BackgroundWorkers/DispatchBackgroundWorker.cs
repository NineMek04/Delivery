using System;
using System.Threading;
using System.Threading.Tasks;
using BackendApi.Services.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace BackendApi.Services.BackgroundWorkers;

public class DispatchBackgroundWorker : BackgroundService
{
    private readonly IDispatchTaskQueue _taskQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DispatchBackgroundWorker> _logger;

    public DispatchBackgroundWorker(
        IDispatchTaskQueue taskQueue,
        IServiceScopeFactory scopeFactory,
        ILogger<DispatchBackgroundWorker> logger)
    {
        _taskQueue = taskQueue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DispatchBackgroundWorker is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var task = await _taskQueue.DequeueAsync(stoppingToken);

                var correlationId = task.CorrelationId ?? Guid.NewGuid().ToString();
                using (LogContext.PushProperty("CorrelationId", correlationId))
                {
                    _logger.LogInformation("Processing background dispatch task: Type = {Type}, Id = {Id}", task.Type, task.Id);

                    using var scope = _scopeFactory.CreateScope();
                    var dispatchSvc = scope.ServiceProvider.GetRequiredService<DispatchService>();

                    switch (task.Type)
                    {
                        case DispatchTaskType.CreateOrder:
                            var batchEvaluator = scope.ServiceProvider.GetRequiredService<BatchEvaluator>();
                            var db = scope.ServiceProvider.GetRequiredService<BackendApi.Data.ApplicationDbContext>();

                            var orderEntity = await db.Orders.FindAsync(new object[] { task.Id }, stoppingToken);
                            if (orderEntity == null)
                            {
                                _logger.LogWarning("Order {OrderId} not found for dispatch task", task.Id);
                                break;
                            }

                            // 1. Try grouping Pre-dispatch Batching
                            var batchId = await batchEvaluator.TryGroupAsync(orderEntity);
                            if (batchId != null)
                            {
                                await dispatchSvc.StartBatchDispatchAsync(batchId);
                            }
                            else
                            {
                                // 2. Try injecting order to picking-up rider
                                var injected = await dispatchSvc.TryInjectOrderAsync(orderEntity.Id);
                                if (!injected)
                                {
                                    // 3. Start normal dispatch
                                    await dispatchSvc.StartDispatchAsync(orderEntity.Id);
                                }
                            }
                            break;

                        case DispatchTaskType.RetryOrder:
                            await dispatchSvc.StartDispatchAsync(task.Id);
                            break;

                        case DispatchTaskType.BatchGroup:
                            await dispatchSvc.StartBatchDispatchAsync(task.Id);
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("DispatchBackgroundWorker is stopping due to cancellation.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing dispatch task.");
            }
        }
    }
}
