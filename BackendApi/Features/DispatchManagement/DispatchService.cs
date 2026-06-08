using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Infrastructure.Redis;
using BackendApi.Models;
using BackendApi.Models.DTOs;
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
    private readonly IAiService _aiService;
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
        IAiService aiService,
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
        _aiService = aiService;
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
        await FindAndOfferAsync(new List<Order> { order });

        return true;
    }

    /// <summary>
    /// เริ่มกระบวนการหา Rider สำหรับ Order แบบพ่วง (Batch)
    /// </summary>
    public async Task<bool> StartBatchDispatchAsync(string batchGroupId)
    {
        var orders = await _dbContext.Orders
            .Where(o => o.BatchGroupId == batchGroupId && o.State == OrderState.CREATED)
            .OrderBy(o => o.BatchSequence)
            .ToListAsync();

        if (orders.Count == 0) return false;

        foreach (var order in orders)
        {
            await _stateMachine.TransitionOrderAsync(order, OrderState.MATCHING);
        }

        await FindAndOfferAsync(orders);
        return true;
    }

    /// <summary>
    /// พยายามแทรกออเดอร์ใหม่ให้ Rider ที่กำลังไปรับของ (Dynamic Injection)
    /// </summary>
    public async Task<bool> TryInjectOrderAsync(string orderId)
    {
        var order = await _dbContext.Orders.FindAsync(orderId);
        if (order is null || order.State != OrderState.CREATED) return false;

        // ดึงไรเดอร์ที่กำลัง PICKING_UP
        var busyRiders = await _dbContext.Riders
            .Where(r => r.State == RiderState.BUSY)
            .ToListAsync();

        foreach (var rider in busyRiders)
        {
            // ตรวจสอบว่า rider มีออเดอร์ในมือที่กำลัง PICKING_UP และยังรับเพิ่มได้ (batch < 3)
            var activeOrders = await _dbContext.Orders
                .Where(o => o.AssignedRiderId == rider.Id && (o.State == OrderState.ASSIGNED || o.State == OrderState.PICKING_UP))
                .ToListAsync();

            var maxActiveOrders = _config.GetValue("Dispatch:MaxActiveOrdersPerRider", 3);
            if (activeOrders.Count == 0 || activeOrders.Count >= maxActiveOrders) continue;

            // ตรวจสอบ Compatibility (Same Shop) - เพื่อความเรียบง่ายในเฟสแรก รองรับเฉพาะร้านเดียวกัน
            if (activeOrders.Any(o => o.ShopId == order.ShopId))
            {
                var batchId = activeOrders.First().BatchGroupId ?? $"BATCH-{Guid.NewGuid():N}"[..16];
                
                // จับกลุ่ม
                if (activeOrders.First().BatchGroupId == null)
                {
                    foreach (var ao in activeOrders)
                    {
                        ao.BatchGroupId = batchId;
                        ao.BatchSequence = 1;
                    }
                }
                order.BatchGroupId = batchId;
                order.BatchSequence = activeOrders.Count + 1;
                await _dbContext.SaveChangesAsync();

                // ยิง Injection Offer ให้ Rider คนนี้
                await TryOfferToRiderAsync(new List<Order> { order }, rider.Id, isInjection: true);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ค้นหา Rider ที่ใกล้ที่สุดและยิง Offer ไปให้
    /// </summary>
    public async Task FindAndOfferAsync(List<Order> orders)
    {
        if (orders == null || orders.Count == 0) return;
        var firstOrder = orders.First();

        if (firstOrder.PickupLocation is null)
        {
            _logger.LogWarning("Order {OrderId} has no pickup location", firstOrder.Id);
            return;
        }

        var searchRadiusKm = _config.GetValue("Dispatch:SearchRadiusKm", 10);
        var pickupLat = firstOrder.PickupLocation.Y;
        var pickupLng = firstOrder.PickupLocation.X;

        // 1. ดึง Nearby Riders จาก Redis GEORADIUS
        var nearbyRiders = await _presenceService.GetNearbyRidersAsync(pickupLat, pickupLng, searchRadiusKm);

        await _adminNotifier.NotifyDispatchScanStartedAsync(firstOrder, pickupLat, pickupLng, searchRadiusKm, nearbyRiders);

        if (nearbyRiders.Length == 0)
        {
            _logger.LogWarning("No nearby riders found for order {OrderId} within {Radius}km",
                firstOrder.Id, searchRadiusKm);
            return;
        }

        // 2. กรองเฉพาะ Rider ที่ IDLE (ไม่ถูกจอง/ไม่มีงาน)
        var candidates = new List<(string RiderId, double DistanceKm, double Lat, double Lng)>();
        
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

            candidates.Add((riderId, result.Distance ?? 0, result.Position?.Latitude ?? 0, result.Position?.Longitude ?? 0));
        }

        if (candidates.Count == 0)
        {
            _logger.LogWarning("No idle riders available for order {OrderId}", firstOrder.Id);
            return;
        }

        // 3. ส่ง Candidates ไป AI Engine สำหรับ Scoring (Phase A)
        var rankedCandidates = await _ranker.RankCandidatesAsync(firstOrder, candidates, ridersDict);

        await _adminNotifier.NotifyCandidatesRankedAsync(firstOrder, rankedCandidates);

        // 4. ลองจอง Rider ทีละคนตามลำดับ
        foreach (var candidate in rankedCandidates)
        {
            var success = await TryOfferToRiderAsync(orders, candidate.RiderId);
            if (success) return; // จองได้แล้ว
        }

        _logger.LogWarning("Could not lock any rider for order {OrderId}", firstOrder.Id);
    }

    /// <summary>
    /// ยิง Offer ไปให้ Rider พร้อม Lock + Timer
    /// </summary>
    private async Task<bool> TryOfferToRiderAsync(List<Order> orders, string riderId, bool isInjection = false)
    {
        var offerTimeout = _config.GetValue("Dispatch:OfferTimeoutSeconds", 30);
        var offerId = $"OFF-{Guid.NewGuid():N}"[..16];
        var timeout = TimeSpan.FromSeconds(offerTimeout);

        // จอง Rider ด้วย Redis Lock — ข้ามกรณี injection เพราะ rider กำลัง BUSY อยู่แล้วและมี lock เดิม
        if (!isInjection)
        {
            if (!await _lockService.TryAcquireRiderLockAsync(riderId, offerId, timeout))
                return false;
        }

        // เปลี่ยนสถานะ Rider → RESERVED (ข้ามถ้าเป็น injection เพราะกำลัง BUSY)
        if (!isInjection)
        {
            if (!await _stateMachine.TransitionRiderAsync(riderId, RiderState.RESERVED))
            {
                await _lockService.ReleaseLockAsync(riderId, offerId);
                return false;
            }
        }

        // อัปเดต Order ด้วย Offer info
        foreach (var order in orders)
        {
            order.CurrentOfferId = offerId;
            order.OfferVersion++;
            order.OfferExpiresAt = DateTime.UtcNow.Add(timeout);
            order.AssignedRiderId = riderId;

            if (!await _stateMachine.TransitionOrderAsync(order, OrderState.OFFERING))
            {
                if (!isInjection) await _lockService.ReleaseLockAsync(riderId, offerId);
                if (!isInjection) await _stateMachine.TransitionRiderAsync(riderId, RiderState.IDLE);
                return false;
            }
        }

        var firstOrder = orders.First();

        // คำนวณเส้นทาง Rider → Pickup
        string? pickupPolyline = null;
        double? pickupRouteDistanceMeters = null;
        double? pickupRouteDurationSeconds = null;
        var riderLocation = await _presenceService.GetLastKnownLocationAsync(riderId);

        if (riderLocation is not null && firstOrder.PickupLocation is not null)
        {
            try
            {
                var pickupRoute = await _routingService.GetRouteDetailsAsync(
                    riderLocation.Value.Lat,
                    riderLocation.Value.Lng,
                    firstOrder.PickupLocation.Y,
                    firstOrder.PickupLocation.X);

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
                    firstOrder.Id);
            }
        }

        // Re-calculate ETA ด้วย OSRM pickup duration + Rider velocity จริง (อัปเดตแค่ order แรกก่อน หรือทั้งหมดถ้าต้องการ)
        if (pickupRouteDurationSeconds.HasValue && firstOrder.RouteDurationSeconds > 0)
        {
            try
            {
                var riderSpeed = await _presenceService.GetRiderSpeedAsync(riderId);
                foreach (var order in orders)
                {
                    var etaRequest = new PredictEtaRequestDto
                    {
                        PickupLat = order.PickupLocation?.Y ?? 0,
                        PickupLng = order.PickupLocation?.X ?? 0,
                        DropoffLat = order.DropoffLocation?.Y ?? 0,
                        DropoffLng = order.DropoffLocation?.X ?? 0,
                        RouteDistanceMeters = order.RouteDistanceMeters,
                        RouteDurationSeconds = order.RouteDurationSeconds,
                        CurrentTime = DateTime.UtcNow.ToString("O"),
                        RiderSpeedKmh = riderSpeed > 0 ? riderSpeed : null,
                        OsrmPickupDurationSeconds = pickupRouteDurationSeconds.Value
                    };

                    var etaResult = await _aiService.PredictEtaAsync(etaRequest);
                    if (etaResult != null && DateTime.TryParse(etaResult.EtaDatetime, out var newEta))
                    {
                        order.ExpectedDeliveryTime = newEta;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to re-calculate ETA for Orders with Rider {RiderId}. Using original ETA.", riderId);
            }
        }

        var offerPayload = new
        {
            OfferId = offerId,
            Version = firstOrder.OfferVersion,
            ExpiresAt = firstOrder.OfferExpiresAt,
            RiderId = riderId,
            IsBatch = orders.Count > 1,
            IsInjection = isInjection,
            BatchGroupId = firstOrder.BatchGroupId,
            PickupRoute = new
            {
                EncodedPolyline = pickupPolyline,
                DistanceMeters = pickupRouteDistanceMeters,
                DurationSeconds = pickupRouteDurationSeconds,
                StartLat = riderLocation?.Lat,
                StartLng = riderLocation?.Lng,
                EndLat = firstOrder.PickupLocation?.Y,
                EndLng = firstOrder.PickupLocation?.X
            },
            Orders = orders.Select(o => new
            {
                o.Id,
                PickupLat = o.PickupLocation?.Y,
                PickupLng = o.PickupLocation?.X,
                DropoffLat = o.DropoffLocation?.Y,
                DropoffLng = o.DropoffLocation?.X,
                o.SlaLimitMinutes,
                DistanceKm = o.DistanceKm,
                DeliveryFee = o.DeliveryFee,
                EncodedPolyline = o.EncodedPolyline,
                Sequence = o.BatchSequence
            }).ToList(),
            TotalDeliveryFee = orders.Sum(o => o.DeliveryFee),
            TotalDistanceKm = orders.Sum(o => o.DistanceKm)
        };

        // ส่ง Offer ไปให้ Rider ผ่าน SignalR
        await _riderNotifier.SendOfferToRiderAsync(riderId, offerPayload);

        // แจ้ง Admin Dashboard
        await _adminNotifier.NotifyOfferSentAsync(offerPayload);

        // Trigger FCM push notification to Rider in background
        _riderNotifier.SendFcmOfferNotificationInBackground(riderId, firstOrder.Id, offerId, orders.Sum(o => o.DeliveryFee), orders.Sum(o => o.DistanceKm));

        _logger.LogInformation(
            "Offer {OfferId} sent to Rider {RiderId} for {Count} Orders (Batch: {BatchId}) — expires in {Timeout}s",
            offerId, riderId, orders.Count, firstOrder.BatchGroupId, offerTimeout);

        return true;
    }
}
