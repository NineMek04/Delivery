using BackendApi.Models;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using Order = BackendApi.Models.Order;

namespace BackendApi.Services.Dispatch;

/// <summary>
/// Admin Notifier — Broadcast สถานะ Dispatch ไปยัง Admin Dashboard ผ่าน SignalR
/// </summary>
public class DispatchAdminNotifier
{
    private readonly IHubContext<BackendApi.Hubs.TrackingHub> _hubContext;
    private readonly ILogger<DispatchAdminNotifier> _logger;

    public DispatchAdminNotifier(
        IHubContext<BackendApi.Hubs.TrackingHub> hubContext,
        ILogger<DispatchAdminNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// แจ้ง Admin ว่าเริ่มสแกน Rider ใกล้เคียง
    /// </summary>
    public virtual async Task NotifyDispatchScanStartedAsync(
        Order order, double pickupLat, double pickupLng, int searchRadiusKm, GeoRadiusResult[] nearbyRiders)
    {
        await _hubContext.Clients.Group("admins").SendAsync("DispatchScanStarted", new
        {
            Order = BuildOrderPayload(order),
            PickupLat = pickupLat,
            PickupLng = pickupLng,
            SearchRadiusKm = searchRadiusKm,
            DispatchAttempt = order.DispatchAttempts,
            NearbyRiders = nearbyRiders.Select(r => new
            {
                RiderId = r.Member.ToString(),
                Lat = r.Position?.Latitude,
                Lng = r.Position?.Longitude,
                DistanceKm = r.Distance
            }).ToList(),
            StartedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// แจ้ง Admin ว่า Candidates ถูกจัดอันดับแล้ว
    /// </summary>
    public virtual async Task NotifyCandidatesRankedAsync(Order order, List<RankedCandidate> rankedCandidates)
    {
        await _hubContext.Clients.Group("admins").SendAsync("DispatchCandidatesRanked", new
        {
            Order = BuildOrderPayload(order),
            RankedCandidates = rankedCandidates.Select((c, index) => new
            {
                Rank = index + 1,
                c.RiderId,
                c.DistanceKm,
                c.Score,
                c.EtaMinutes
            }).ToList(),
            RankedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// แจ้ง Admin ว่า Offer ถูกส่งไปให้ Rider แล้ว
    /// </summary>
    public virtual async Task NotifyOfferSentAsync(object offerPayload)
    {
        await _hubContext.Clients.Group("admins").SendAsync("DispatchOfferSent", offerPayload);
    }

    /// <summary>
    /// แจ้ง Admin ว่า Order ถูก Assign ให้ Rider แล้ว
    /// </summary>
    public virtual async Task NotifyOrderAssignedAsync(string orderId, string riderId, DateTime? assignedAt)
    {
        await _hubContext.Clients.Group("admins").SendAsync("OrderAssigned", new
        {
            Id = orderId,
            RiderId = riderId,
            AssignedAt = assignedAt
        });
    }

    /// <summary>
    /// สร้าง Payload สำหรับ Order ที่จะส่งไป Admin Dashboard
    /// </summary>
    internal static object BuildOrderPayload(Order order)
    {
        return new
        {
            order.Id,
            PickupLat = order.PickupLocation?.Y,
            PickupLng = order.PickupLocation?.X,
            DropoffLat = order.DropoffLocation?.Y,
            DropoffLng = order.DropoffLocation?.X,
            order.SlaLimitMinutes,
            DistanceKm = order.DistanceKm,
            DeliveryFee = order.DeliveryFee,
            EncodedPolyline = order.EncodedPolyline,
            RouteDistanceMeters = order.RouteDistanceMeters,
            RouteDurationSeconds = order.RouteDurationSeconds
        };
    }
}
