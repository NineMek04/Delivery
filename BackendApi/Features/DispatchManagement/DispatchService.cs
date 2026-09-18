using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Infrastructure.Redis;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using BackendApi.Security.Models;
using BackendApi.Security.Services;
using BackendApi.Services.Ai;
using BackendApi.Services.Telemetry;
using Microsoft.EntityFrameworkCore;
using Order = BackendApi.Models.Entities.Order;

namespace BackendApi.Services.Dispatch;

/// <summary>
/// Dispatch Orchestrator — คุม Flow การจับคู่ Rider กับ Order ทั้งหมด (The Heart)
/// 
/// Flow 30 วินาที:
/// 1. Order ใหม่ → เปลี่ยนเป็น MATCHING
/// 2. ดึง Nearby Idle Riders จาก Redis GEORADIUS
/// 3. ส่ง Candidates ให้ระบบจัดอันดับตามความเหมาะสม (async, ไม่ block SignalR)
/// 4. จอง Rider อันดับ 1 (Redis SETNX)
/// 5. ยิง Offer ผ่าน SignalR + OfferId + Version
/// 6. Accept → ASSIGNED / Timeout → Re-dispatch
/// 
/// Delegates:
///   - Heuristic Ranking → DispatchCandidateRanker
///   - Rider SignalR/FCM → DispatchRiderNotifier
///   - Admin SignalR     → DispatchAdminNotifier
///   - Rider Actions     → DispatchOfferHandler (Accept/Reject/Timeout)
/// </summary>
public partial class DispatchService
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
        using var scope = BeginLogScope(orderId);
        var order = await _dbContext.Orders.FindAsync(orderId);
        if (order is null || (order.State != OrderState.CREATED && order.State != OrderState.MATCHING))
        {
            _logger.LogWarning("Cannot dispatch order {OrderId}: not found or invalid state {State}",
                orderId, order?.State);
            return false;
        }

        // เปลี่ยนสถานะเป็น MATCHING
        if (order.State == OrderState.CREATED)
        {
            if (!await _stateMachine.TransitionOrderAsync(order, OrderState.MATCHING))
                return false;
        }

        // ค้นหา Rider ที่อยู่ใกล้
        await FindAndOfferAsync(new List<Order> { order });

        return true;
    }

    /// <summary>
    /// เริ่มกระบวนการหา Rider สำหรับ Order แบบพ่วง (Batch)
    /// </summary>
    public async Task<bool> StartBatchDispatchAsync(string batchGroupId)
    {
        using var scope = BeginLogScope(batchGroupId);
        var orders = await _dbContext.Orders
            .Where(o => o.BatchGroupId == batchGroupId && (o.State == OrderState.CREATED || o.State == OrderState.MATCHING))
            .OrderBy(o => o.BatchSequence)
            .ToListAsync();

        if (orders.Count == 0) return false;

        foreach (var order in orders)
        {
            if (order.State == OrderState.CREATED)
            {
                await _stateMachine.TransitionOrderAsync(order, OrderState.MATCHING);
            }
        }

        await FindAndOfferAsync(orders);
        return true;
    }

    /// <summary>
    /// พยายามแทรกออเดอร์ใหม่ให้ Rider ที่กำลังไปรับของ (Dynamic Injection)
    /// </summary>
    public async Task<bool> TryInjectOrderAsync(string orderId)
    {
        using var scope = BeginLogScope(orderId);
        var order = await _dbContext.Orders.FindAsync(orderId);
        if (order is null || (order.State != OrderState.CREATED && order.State != OrderState.MATCHING)) return false;

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
                // [RACE CONDITION FIX #1] Acquire a short-lived injection lock on this rider
                // before writing BatchGroupId. Two concurrent inject requests for the same
                // BUSY rider would both pass the ShopId check above because neither modifies
                // the rider row — a Redis SETNX with a 3-second TTL serialises them safely.
                var injectionLockKey = $"dispatch:inject_lock:rider:{rider.Id}";
                var injectionLockId  = $"INJ-{Guid.NewGuid():N}"[..12];
                var injectionLockTtl = TimeSpan.FromSeconds(3); // shortest viable window
                if (!await _lockService.TryAcquireRiderLockAsync(injectionLockKey, injectionLockId, injectionLockTtl))
                {
                    _logger.LogDebug("Injection lock contention for Rider {RiderId} — skipping concurrent inject.", rider.Id);
                    continue;
                }

                try
                {
                var batchId = activeOrders.First().BatchGroupId ?? $"BATCH-{Guid.NewGuid():N}"[..16];
                
                // จับกลุ่ม
                if (activeOrders.First().BatchGroupId == null)
                {
                    int seq = 1;
                    foreach (var ao in activeOrders)
                    {
                        ao.BatchGroupId = batchId;
                        ao.BatchSequence = seq++;
                    }
                }
                order.BatchGroupId = batchId;
                order.BatchSequence = activeOrders.Count + 1;

                // [STATE MACHINE FIX #2] CREATED → MATCHING is required before OFFERING.
                // Without this transition, TransitionOrderAsync(OFFERING) returns false
                // (OrderStateRules rejects CREATED → OFFERING) and the order stays CREATED
                // forever while BatchGroupId is already written — causing a Ghost Batch.
                if (order.State != OrderState.MATCHING && !await _stateMachine.TransitionOrderAsync(order, OrderState.MATCHING))
                {
                    order.BatchGroupId  = null;
                    order.BatchSequence = 0;
                    _logger.LogWarning("TryInjectOrderAsync: CREATED→MATCHING transition failed for Order {OrderId}.", orderId);
                    return false;
                }

                await _dbContext.SaveChangesAsync();

                // ยิง Injection Offer ให้ Rider คนนี้
                var offerSuccess = await TryOfferToRiderAsync(new List<Order> { order }, rider.Id, isInjection: true);
                if (!offerSuccess)
                {
                    // Rollback dynamic injection batch pairing
                    order.BatchGroupId = null;
                    order.BatchSequence = 0;
                    await _dbContext.SaveChangesAsync();
                    return false;
                }
                return true;
                }
                finally
                {
                    await _lockService.ReleaseLockAsync(injectionLockKey, injectionLockId);
                }
            }
        }

        return false;
    }

    private IDisposable? BeginLogScope(string? orderId, string? riderId = null)
    {
        return _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = CorrelationIdProvider.GetOrCreate((HttpContext?)null),
            ["OrderId"] = orderId,
            ["RiderId"] = riderId
        });
    }
}
