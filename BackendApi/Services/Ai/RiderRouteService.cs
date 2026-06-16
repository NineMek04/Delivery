using BackendApi.Data;
using BackendApi.Features.AiRouting;
using BackendApi.Security.Models;
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
        var dbUser = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.RiderId, user.Role })
            .SingleOrDefaultAsync(cancellationToken);

        if (dbUser is null)
        {
            return null;
        }

        var order = await _dbContext.Orders
            .AsNoTracking()
            .Where(item => item.Id == request.OrderId)
            .Select(item => new
            {
                item.Id,
                item.PickupLocation,
                item.DropoffLocation,
                item.CustomerId,
                item.AssignedRiderId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return null;
        }

        bool isAllowed = false;
        if (dbUser.Role == AuthConstants.AdminRole || dbUser.Role == AuthConstants.DispatcherRole)
        {
            isAllowed = true;
        }
        else if (order.CustomerId == userId)
        {
            isAllowed = true;
        }
        else if (!string.IsNullOrWhiteSpace(dbUser.RiderId) && order.AssignedRiderId == dbUser.RiderId)
        {
            isAllowed = true;
        }

        if (!isAllowed)
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
            dbUser.RiderId ?? order.AssignedRiderId ?? string.Empty,
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
