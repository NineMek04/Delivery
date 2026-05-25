using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Core.StateMachines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BackendApi.HealthChecks;

public class DispatchQueueHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _dbContext;

    public DispatchQueueHealthCheck(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var pendingCount = await _dbContext.Orders
                .CountAsync(o => o.State == OrderState.MATCHING || o.State == OrderState.OFFERING, cancellationToken);

            var data = new Dictionary<string, object>
            {
                { "PendingDispatch", pendingCount }
            };

            // หากมีคิวค้างเยอะเกินไป อาจจะเตือนเป็น Degraded
            if (pendingCount > 100)
            {
                return HealthCheckResult.Degraded("High dispatch queue volume", null, data);
            }

            return HealthCheckResult.Healthy("Dispatch queue is normal", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Failed to read dispatch queue", ex);
        }
    }
}
