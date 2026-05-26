using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace BackendApi.Services.Dispatch;

/// <summary>
/// Dispatch Offer Handler — จัดการ Action ที่ Rider กดจากฝั่ง App (Accept / Reject)
/// แยกออกจาก DispatchService Orchestrator เพื่อให้แต่ละคลาสมีหน้าที่เดียว
/// 
/// Callers:
///   - TrackingHub.Dispatch.cs (AcceptOffer, RejectOffer)
///   - DispatchTimeoutWorker   (RejectOrTimeoutAsync)
///   - HeartbeatMonitor        (RejectOrTimeoutAsync)
/// </summary>
public class DispatchOfferHandler
{
    private readonly ApplicationDbContext _dbContext;
    private readonly StateMachineService _stateMachine;
    private readonly RedisLockService _lockService;
    private readonly DispatchAdminNotifier _adminNotifier;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<DispatchOfferHandler> _logger;

    // DispatchService reference — ใช้สำหรับ Re-dispatch หลัง reject/timeout
    private readonly IServiceProvider _serviceProvider;

    public DispatchOfferHandler(
        ApplicationDbContext dbContext,
        StateMachineService stateMachine,
        RedisLockService lockService,
        DispatchAdminNotifier adminNotifier,
        IConnectionMultiplexer redis,
        IServiceProvider serviceProvider,
        ILogger<DispatchOfferHandler> logger)
    {
        _dbContext = dbContext;
        _stateMachine = stateMachine;
        _lockService = lockService;
        _adminNotifier = adminNotifier;
        _redis = redis;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Rider กดรับงาน — validate OfferId + Version ก่อน assign
    /// </summary>
    public async Task<bool> AcceptOfferAsync(string riderId, string offerId, int version)
    {
        var db = _redis.GetDatabase();
        var lockKey = $"lock:accept:offer:{offerId}";

        // 1. ดึง Redis Distributed Lock เพื่อป้องกัน Race Condition จากการกดยอมรับพร้อมกัน
        var acquired = await db.StringSetAsync(lockKey, "locked", TimeSpan.FromSeconds(5), When.NotExists);
        if (!acquired)
        {
            _logger.LogWarning("Accept failed: concurrent accept attempt for offer {OfferId} by Rider {RiderId}", offerId, riderId);
            return false;
        }

        try
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

            try
            {
                // เปลี่ยนสถานะออเดอร์
                if (!await _stateMachine.TransitionOrderAsync(order, OrderState.ASSIGNED))
                    return false;

                if (!await _stateMachine.TransitionRiderAsync(riderId, RiderState.BUSY))
                    return false;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, "Concurrency collision detected while Rider {RiderId} was accepting offer {OfferId} for Order {OrderId}", 
                    riderId, offerId, order.Id);
                return false; // Concurrency conflict handled safely
            }

            // แจ้ง Admin Dashboard
            await _adminNotifier.NotifyOrderAssignedAsync(order.Id, riderId, order.AssignedAt);

            _logger.LogInformation(
                "Order {OrderId} assigned to Rider {RiderId} via offer {OfferId}",
                order.Id, riderId, offerId);

            return true;
        }
        finally
        {
            // ปลดล็อค Lock
            await db.KeyDeleteAsync(lockKey);
        }
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

            // Re-dispatch: หาคนถัดไป — resolve DispatchService via DI เพื่อหลีกเลี่ยง circular dependency
            var dispatchService = _serviceProvider.GetRequiredService<DispatchService>();
            await dispatchService.FindAndOfferAsync(order);
        }
    }
}
