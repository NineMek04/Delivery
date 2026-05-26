using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Infrastructure.Redis;
using BackendApi.Models;
using BackendApi.Services.Ai;
using Microsoft.EntityFrameworkCore;
using Order = BackendApi.Models.Order;

namespace BackendApi.Services.Dispatch;

/// <summary>
/// Dispatch Orchestrator — คุม Flow การจับคู่ Rider กับ Order ทั้งหมด (The Heart)
/// 
/// Flow 30 วินาที:
/// 1. Order ใหม่ → เปลี่ยนเป็น MATCHING
/// 2. ดึง Nearby Idle Riders จาก Redis GEORADIUS
/// 3. ส่ง Candidates ให้ AI Score (async, ไม่ block SignalR)
/// 4. จอง Rider อันดับ 1 (Redis SETNX)
/// 5. ยิง Offer ผ่าน SignalR + OfferId + Version
/// 6. Accept → ASSIGNED / Timeout → Re-dispatch
/// 
/// Delegates:
///   - AI Ranking        → DispatchCandidateRanker
///   - Rider SignalR/FCM → DispatchRiderNotifier
///   - Admin SignalR     → DispatchAdminNotifier
///   - Rider Actions     → DispatchOfferHandler (Accept/Reject/Timeout)
/// </summary>
public class DispatchService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly StateMachineService _stateMachine;
    private readonly RedisLockService _lockService;
    private readonly RiderPresenceService _presenceService;
    private readonly OsrmRoutingService _routingService;
    private readonly DispatchCandidateRanker _ranker;
    private readonly DispatchRiderNotifier _riderNotifier;
    private readonly DispatchAdminNotifier _adminNotifier;
    private readonly IConfiguration _config;
    private readonly ILogger<DispatchService> _logger;

    public DispatchService(
        ApplicationDbContext dbContext,
        StateMachineService stateMachine,
        RedisLockService lockService,
        RiderPresenceService presenceService,
        OsrmRoutingService routingService,
        DispatchCandidateRanker ranker,
        DispatchRiderNotifier riderNotifier,
        DispatchAdminNotifier adminNotifier,
        IConfiguration config,
        ILogger<DispatchService> logger)
    {
        _dbContext = dbContext;
        _stateMachine = stateMachine;
        _lockService = lockService;
        _presenceService = presenceService;
        _routingService = routingService;
        _ranker = ranker;
        _riderNotifier = riderNotifier;
        _adminNotifier = adminNotifier;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// เริ่มกระบวนการหา Rider สำหรับ Order
    /// </summary>
    public async Task<bool> StartDispatchAsync(string orderId)
    {
        var order = await _dbContext.Orders.FindAsync(orderId);
        if (order is null || order.State != OrderState.CREATED)
        {
            _logger.LogWarning("Cannot dispatch order {OrderId}: not found or invalid state {State}",
                orderId, order?.State);
            return false;
        }

        // เปลี่ยนสถานะเป็น MATCHING
        if (!await _stateMachine.TransitionOrderAsync(order, OrderState.MATCHING))
            return false;

        // ค้นหา Rider ที่อยู่ใกล้
        await FindAndOfferAsync(order);

        return true;
    }

    /// <summary>
    /// ค้นหา Rider ที่ใกล้ที่สุดและยิง Offer ไปให้
    /// </summary>
    public async Task FindAndOfferAsync(Order order)
    {
        if (order.PickupLocation is null)
        {
            _logger.LogWarning("Order {OrderId} has no pickup location", order.Id);
            return;
        }

        var searchRadiusKm = _config.GetValue("Dispatch:SearchRadiusKm", 10);
        var pickupLat = order.PickupLocation.Y;
        var pickupLng = order.PickupLocation.X;

        // 1. ดึง Nearby Riders จาก Redis GEORADIUS
        var nearbyRiders = await _presenceService.GetNearbyRidersAsync(pickupLat, pickupLng, searchRadiusKm);

        await _adminNotifier.NotifyDispatchScanStartedAsync(order, pickupLat, pickupLng, searchRadiusKm, nearbyRiders);

        if (nearbyRiders.Length == 0)
        {
            _logger.LogWarning("No nearby riders found for order {OrderId} within {Radius}km",
                order.Id, searchRadiusKm);
            return;
        }

        // 2. กรองเฉพาะ Rider ที่ IDLE (ไม่ถูกจอง/ไม่มีงาน)
        var candidates = new List<(string RiderId, double DistanceKm)>();
        
        var riderIds = nearbyRiders.Select(r => r.Member.ToString()).ToList();
        var ridersDict = await _dbContext.Riders
            .Where(r => riderIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id);

        foreach (var result in nearbyRiders)
        {
            var riderId = result.Member.ToString();
            
            if (!ridersDict.TryGetValue(riderId, out var rider))
                continue;

            if (rider.State != RiderState.IDLE)
                continue;

            if (await _lockService.IsLockedAsync(riderId))
                continue;

            candidates.Add((riderId, result.Distance ?? 0));
        }

        if (candidates.Count == 0)
        {
            _logger.LogWarning("No idle riders available for order {OrderId}", order.Id);
            return;
        }

        // 3. ส่ง Candidates ไป AI Engine สำหรับ Scoring (Phase A)
        var rankedCandidates = await _ranker.RankCandidatesAsync(order, candidates, ridersDict);

        await _adminNotifier.NotifyCandidatesRankedAsync(order, rankedCandidates);

        // 4. ลองจอง Rider ทีละคนตามลำดับ
        foreach (var candidate in rankedCandidates)
        {
            var success = await TryOfferToRiderAsync(order, candidate.RiderId);
            if (success) return; // จองได้แล้ว
        }

        _logger.LogWarning("Could not lock any rider for order {OrderId}", order.Id);
    }

    /// <summary>
    /// ยิง Offer ไปให้ Rider พร้อม Lock + Timer
    /// </summary>
    private async Task<bool> TryOfferToRiderAsync(Order order, string riderId)
    {
        var offerTimeout = _config.GetValue("Dispatch:OfferTimeoutSeconds", 30);
        var offerId = $"OFF-{Guid.NewGuid():N}"[..16];
        var timeout = TimeSpan.FromSeconds(offerTimeout);

        // จอง Rider ด้วย Redis Lock
        if (!await _lockService.TryAcquireRiderLockAsync(riderId, offerId, timeout))
            return false;

        // เปลี่ยนสถานะ Rider → RESERVED
        if (!await _stateMachine.TransitionRiderAsync(riderId, RiderState.RESERVED))
        {
            await _lockService.ReleaseLockAsync(riderId, offerId);
            return false;
        }

        // อัปเดต Order ด้วย Offer info
        order.CurrentOfferId = offerId;
        order.OfferVersion++;
        order.OfferExpiresAt = DateTime.UtcNow.Add(timeout);
        order.AssignedRiderId = riderId;

        if (!await _stateMachine.TransitionOrderAsync(order, OrderState.OFFERING))
        {
            await _lockService.ReleaseLockAsync(riderId, offerId);
            await _stateMachine.TransitionRiderAsync(riderId, RiderState.IDLE);
            return false;
        }

        // คำนวณเส้นทาง Rider → Pickup
        string? pickupPolyline = null;
        double? pickupRouteDistanceMeters = null;
        double? pickupRouteDurationSeconds = null;
        var riderLocation = await _presenceService.GetLastKnownLocationAsync(riderId);

        if (riderLocation is not null && order.PickupLocation is not null)
        {
            try
            {
                var pickupRoute = await _routingService.GetRouteDetailsAsync(
                    riderLocation.Value.Lat,
                    riderLocation.Value.Lng,
                    order.PickupLocation.Y,
                    order.PickupLocation.X);

                pickupPolyline = pickupRoute.Polyline;
                pickupRouteDistanceMeters = pickupRoute.DistanceMeters;
                pickupRouteDurationSeconds = pickupRoute.DurationSeconds;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to calculate pickup route for Rider {RiderId} and Order {OrderId}. Falling back to straight line on clients.",
                    riderId,
                    order.Id);
            }
        }

        var offerPayload = new
        {
            OfferId = offerId,
            Version = order.OfferVersion,
            ExpiresAt = order.OfferExpiresAt,
            RiderId = riderId,
            PickupRoute = new
            {
                EncodedPolyline = pickupPolyline,
                DistanceMeters = pickupRouteDistanceMeters,
                DurationSeconds = pickupRouteDurationSeconds,
                StartLat = riderLocation?.Lat,
                StartLng = riderLocation?.Lng,
                EndLat = order.PickupLocation?.Y,
                EndLng = order.PickupLocation?.X
            },
            Order = new
            {
                order.Id,
                PickupLat = order.PickupLocation?.Y,
                PickupLng = order.PickupLocation?.X,
                DropoffLat = order.DropoffLocation?.Y,
                DropoffLng = order.DropoffLocation?.X,
                order.SlaLimitMinutes,
                DistanceKm = order.DistanceKm,
                DeliveryFee = order.DeliveryFee,
                EncodedPolyline = order.EncodedPolyline
            }
        };

        // ส่ง Offer ไปให้ Rider ผ่าน SignalR
        await _riderNotifier.SendOfferToRiderAsync(riderId, offerPayload);

        // แจ้ง Admin Dashboard
        await _adminNotifier.NotifyOfferSentAsync(offerPayload);

        // Trigger FCM push notification to Rider in background
        _riderNotifier.SendFcmOfferNotificationInBackground(riderId, order.Id, offerId, order.DeliveryFee, order.DistanceKm);

        _logger.LogInformation(
            "Offer {OfferId} (v{Version}) sent to Rider {RiderId} for Order {OrderId} — expires in {Timeout}s",
            offerId, order.OfferVersion, riderId, order.Id, offerTimeout);

        return true;
    }
}
