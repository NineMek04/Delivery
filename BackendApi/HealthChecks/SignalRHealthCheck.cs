using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Core.StateMachines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BackendApi.HealthChecks;

public class SignalRHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _dbContext;

    public SignalRHealthCheck(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var activeRidersCount = await _dbContext.Riders
                .CountAsync(r => r.State != RiderState.OFFLINE, cancellationToken);

            var data = new Dictionary<string, object>
            {
                { "ActiveRiders", activeRidersCount }
            };

            return HealthCheckResult.Healthy("SignalR tracking is active", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Failed to read active riders count", ex);
        }
    }
}
