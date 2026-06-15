using BackendApi.Data;
using BackendApi.Features.FleetTracking.Models;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Services.Telemetry;

public sealed class ClientRouteTelemetryService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ClientRouteTelemetryService> _logger;

    public ClientRouteTelemetryService(
        ApplicationDbContext dbContext,
        ILogger<ClientRouteTelemetryService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<bool> ReportFallbackAsync(
        string userId,
        ClientRouteFallbackRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var riderId = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.RiderId)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(riderId))
        {
            return false;
        }

        var isAssignedOrder = await _dbContext.Orders
            .AsNoTracking()
            .AnyAsync(
                order => order.Id == request.OrderId &&
                         order.AssignedRiderId == riderId,
                cancellationToken);

        if (!isAssignedOrder)
        {
            return false;
        }

        OperationalMetrics.ClientRouteFallbacksTotal.WithLabels(request.Reason, request.RoutePhase).Inc();

        _logger.LogWarning(
            "Rider route fallback rendered. CorrelationId={CorrelationId} OrderId={OrderId} RiderId={RiderId} RoutePhase={RoutePhase} Reason={Reason} EncodedLength={EncodedLength}",
            correlationId,
            request.OrderId,
            riderId,
            request.RoutePhase,
            request.Reason,
            request.EncodedLength);

        return true;
    }
}
