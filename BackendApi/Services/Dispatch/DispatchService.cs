using System.Text.Json;
using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Infrastructure.Redis;
using BackendApi.Models;
using BackendApi.Services.Ai;
using BackendApi.Services.Notifications;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

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
/// </summary>
public class DispatchService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly StateMachineService _stateMachine;
    private readonly RedisLockService _lockService;
    private readonly RiderPresenceService _presenceService;
    private readonly IHubContext<BackendApi.Hubs.TrackingHub> _hubContext;
    private readonly BackendApi.Services.Ai.IAiService _aiService;
    private readonly OsrmRoutingService _routingService;
    private readonly IConfiguration _config;
    private readonly IFcmNotificationService _fcmService;
    private readonly ILogger<DispatchService> _logger;

    public DispatchService(
        ApplicationDbContext dbContext,
        StateMachineService stateMachine,
        RedisLockService lockService,
        RiderPresenceService presenceService,
        IHubContext<BackendApi.Hubs.TrackingHub> hubContext,
        BackendApi.Services.Ai.IAiService aiService,
        OsrmRoutingService routingService,
        IConfiguration config,
        IFcmNotificationService fcmService,
        ILogger<DispatchService> logger)
    {
        _dbContext = dbContext;
        _stateMachine = stateMachine;
        _lockService = lockService;
        _presenceService = presenceService;
        _hubContext = hubContext;
        _aiService = aiService;
        _routingService = routingService;
        _config = config;
        _fcmService = fcmService;
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

        await _hubContext.Clients.Group("admins").SendAsync("DispatchScanStarted", new
        {
            Order = BuildOrderPayload(order),
            PickupLat = pickupLat,
            PickupLng = pickupLng,
            SearchRadiusKm = searchRadiusKm,
            NearbyRiders = nearbyRiders.Select(r => new
            {
                RiderId = r.Member.ToString(),
                Lat = r.Position?.Latitude,
                Lng = r.Position?.Longitude,
                DistanceKm = r.Distance
            }).ToList(),
            StartedAt = DateTime.UtcNow
        });

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
        var rankedCandidates = await RankCandidatesWithAiAsync(order, candidates, ridersDict);

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

        await _hubContext.Clients.Group($"rider:{riderId}")
            .SendAsync("OfferReceived", offerPayload);

        await _hubContext.Clients.Group("admins")
            .SendAsync("DispatchOfferSent", offerPayload);

        // Trigger FCM push notification to Rider in background
        _ = Task.Run(async () =>
        {
            try
            {
                var riderUser = await _dbContext.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.RiderId == riderId);

                if (riderUser != null)
                {
                    await _fcmService.SendNotificationToUserAsync(
                        riderUser.Id,
                        "มีข้อเสนองานใหม่!",
                        $"ค่าบริการจัดส่ง: ฿{order.DeliveryFee} | ระยะทาง: {order.DistanceKm:F1} กม.",
                        new Dictionary<string, string>
                        {
                            { "offerId", offerId },
                            { "orderId", order.Id },
                            { "type", "NEW_OFFER" }
                        });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send FCM offer notification to Rider {RiderId}", riderId);
            }
        });

        _logger.LogInformation(
            "Offer {OfferId} (v{Version}) sent to Rider {RiderId} for Order {OrderId} — expires in {Timeout}s",
            offerId, order.OfferVersion, riderId, order.Id, offerTimeout);

        return true;
    }

    private static object BuildOrderPayload(Order order)
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

    /// <summary>
    /// Rider กดรับงาน — validate OfferId + Version ก่อน assign
    /// </summary>
    public async Task<bool> AcceptOfferAsync(string riderId, string offerId, int version)
    {
        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(o =>
                o.CurrentOfferId == offerId &&
                o.AssignedRiderId == riderId &&
                o.State == OrderState.OFFERING);

        if (order is null)
        {
            _logger.LogWarning("Accept failed: Offer {OfferId} not found or invalid state", offerId);
            return false;
        }

        // Validate version (ป้องกัน stale accept)
        if (order.OfferVersion != version)
        {
            _logger.LogWarning(
                "Accept failed: version mismatch — expected {Expected}, got {Got}",
                order.OfferVersion, version);
            return false;
        }

        // ตรวจ expiration
        if (order.OfferExpiresAt.HasValue && DateTime.UtcNow > order.OfferExpiresAt.Value)
        {
            _logger.LogWarning("Accept failed: Offer {OfferId} has expired", offerId);
            return false;
        }

        // เปลี่ยนสถานะ
        if (!await _stateMachine.TransitionOrderAsync(order, OrderState.ASSIGNED))
            return false;

        if (!await _stateMachine.TransitionRiderAsync(riderId, RiderState.BUSY))
            return false;

        // แจ้ง Admin
        await _hubContext.Clients.Group("admins").SendAsync("OrderAssigned", new
        {
            order.Id,
            RiderId = riderId,
            AssignedAt = order.AssignedAt
        });

        _logger.LogInformation(
            "Order {OrderId} assigned to Rider {RiderId} via offer {OfferId}",
            order.Id, riderId, offerId);

        return true;
    }

    /// <summary>
    /// Rider กดปฏิเสธ / Timeout → ปลดล็อค → Re-dispatch
    /// </summary>
    public async Task RejectOrTimeoutAsync(string orderId, string riderId, string offerId)
    {
        // ปลดล็อค Rider
        await _lockService.ReleaseLockAsync(riderId, offerId);
        await _stateMachine.TransitionRiderAsync(riderId, RiderState.IDLE);

        // เปลี่ยน Order กลับเป็น MATCHING → หาคนใหม่
        var order = await _dbContext.Orders.FindAsync(orderId);
        if (order is null || order.State != OrderState.OFFERING) return;

        if (await _stateMachine.TransitionOrderAsync(order, OrderState.MATCHING))
        {
            _logger.LogInformation(
                "Order {OrderId} re-dispatching after rejection/timeout from Rider {RiderId}",
                orderId, riderId);

            // Re-dispatch: หาคนถัดไป
            await FindAndOfferAsync(order);
        }
    }

    /// <summary>
    /// ส่งรายชื่อ Candidates ไปให้ AI Engine เพื่อให้คะแนนและจัดอันดับ (Phase A Heuristic)
    /// </summary>
    private async Task<List<(string RiderId, double DistanceKm, double Score, int EtaMinutes)>> RankCandidatesWithAiAsync(
        Order order, List<(string RiderId, double DistanceKm)> candidates, Dictionary<string, Rider> ridersDict)
    {
        try
        {
            var request = new BackendApi.Models.DTOs.DispatchRankRequestDto
            {
                Context = new BackendApi.Models.DTOs.DispatchContextDto
                {
                    Timestamp = DateTime.UtcNow.ToString("O"),
                    City = "Bangkok"
                },
                Order = new BackendApi.Models.DTOs.DispatchOrderDto
                {
                    Id = order.Id,
                    Pickup = new List<double> { order.PickupLocation?.Y ?? 0, order.PickupLocation?.X ?? 0 },
                    Dropoff = new List<double> { order.DropoffLocation?.Y ?? 0, order.DropoffLocation?.X ?? 0 },
                    SlaLimitMinutes = order.SlaLimitMinutes
                },
                Candidates = candidates.Select(c =>
                {
                    ridersDict.TryGetValue(c.RiderId, out var rider);
                    return new BackendApi.Models.DTOs.DispatchCandidateDto
                    {
                        RiderId = c.RiderId,
                        Lat = rider?.CurrentLocation?.Y ?? 0,
                        Lng = rider?.CurrentLocation?.X ?? 0,
                        CurrentTasks = new List<Dictionary<string, object>>() // TODO: ดึงงานที่กำลังทำอยู่
                    };
                }).ToList()
            };

            var aiResponse = await _aiService.RankDispatchCandidatesAsync(request);

            if (aiResponse is not null && aiResponse.RankedCandidates.Any())
            {
                var rankedList = new List<(string RiderId, double DistanceKm, double Score, int EtaMinutes)>();
                foreach (var item in aiResponse.RankedCandidates)
                {
                    if (!string.IsNullOrEmpty(item.RiderId))
                    {
                        rankedList.Add((item.RiderId, item.DistanceToPickupKm, item.Score, item.EtaMinutes));
                    }
                }
                return rankedList;
            }
            
            _logger.LogWarning("AI Engine returned null or empty. Falling back to distance-based ranking.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling AI Engine for ranking. Falling back to distance-based ranking.");
        }

        // Fallback: เรียงตามระยะทางที่ได้จาก Redis GEORADIUS พร้อมสร้างคะแนนและเวลาส่งจำลอง
        return candidates.OrderBy(c => c.DistanceKm)
                         .Select(c => (c.RiderId, c.DistanceKm, Math.Max(0.0, 100.0 - c.DistanceKm * 5.0), (int)Math.Ceiling(c.DistanceKm * 2.0)))
                         .ToList();
    }
}
