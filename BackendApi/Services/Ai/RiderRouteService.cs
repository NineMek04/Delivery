using BackendApi.Data;
using BackendApi.Features.AiRouting;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Services.Ai;

public sealed class RiderRouteService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly OsrmRoutingService _routingService;
    private readonly ILogger<RiderRouteService> _logger;

    public RiderRouteService(
        ApplicationDbContext dbContext,
        OsrmRoutingService routingService,
        ILogger<RiderRouteService> logger)
    {
        _dbContext = dbContext;
        _routingService = routingService;
        _logger = logger;
    }

    public async Task<RiderRouteResponse?> ResolveAsync(
        string userId,
        RiderRouteRequest request,
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
            return null;
        }

        var order = await _dbContext.Orders
            .AsNoTracking()
            .Where(item => item.Id == request.OrderId &&
                           item.AssignedRiderId == riderId)
            .Select(item => new
            {
                item.Id,
                item.PickupLocation,
                item.DropoffLocation
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return null;
        }

        var target = request.RoutePhase == "PICKUP"
            ? order.PickupLocation
            : order.DropoffLocation;

        if (target is null)
        {
            return null;
        }

        var route = await _routingService.GetRouteDetailsAsync(
            request.CurrentLat,
            request.CurrentLng,
            target.Y,
            target.X);

        var source = string.IsNullOrWhiteSpace(route.Polyline)
            ? "HAVERSINE_FALLBACK"
            : "LOCAL_OSRM";

        _logger.LogInformation(
            "Rider route resolved. CorrelationId={CorrelationId} OrderId={OrderId} RiderId={RiderId} RoutePhase={RoutePhase} Source={Source}",
            correlationId,
            order.Id,
            riderId,
            request.RoutePhase,
            source);

        return new RiderRouteResponse
        {
            EncodedPolyline = route.Polyline,
            DistanceMeters = route.DistanceMeters,
            DurationSeconds = route.DurationSeconds,
            Source = source
        };
    }
}
