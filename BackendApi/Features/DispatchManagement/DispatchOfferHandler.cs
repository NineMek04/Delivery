using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        var lockKey = $"lock:offer:{offerId}";

        // 1. ดึง Redis Distributed Lock เพื่อป้องกัน Race Condition จากการกดยอมรับพร้อมกัน
        var acquired = await db.StringSetAsync(lockKey, "locked", TimeSpan.FromSeconds(5), When.NotExists);
        if (!acquired)
        {
            _logger.LogWarning("Accept failed: concurrent accept attempt for offer {OfferId} by Rider {RiderId}", offerId, riderId);
            return false;
        }

        try
        {
            var orders = await _dbContext.Orders
                .Where(o =>
                    o.CurrentOfferId == offerId &&
                    o.AssignedRiderId == riderId &&
                    o.State == OrderState.OFFERING)
                .ToListAsync();

            if (orders.Count == 0)
            {
                _logger.LogWarning("Accept failed: Offer {OfferId} not found or invalid state", offerId);
                return false;
            }

            var firstOrder = orders.First();

            // Validate version (ป้องกัน stale accept)
            if (firstOrder.OfferVersion != version)
            {
                _logger.LogWarning(
                    "Accept failed: version mismatch — expected {Expected}, got {Got}",
                    firstOrder.OfferVersion, version);
                return false;
            }

            // ตรวจ expiration
            if (firstOrder.OfferExpiresAt.HasValue && DateTime.UtcNow > firstOrder.OfferExpiresAt.Value)
            {
                _logger.LogWarning("Accept failed: Offer {OfferId} has expired", offerId);
                return false;
            }

            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                // เปลี่ยนสถานะทุกออเดอร์
                foreach (var order in orders)
                {
                    if (!await _stateMachine.TransitionOrderAsync(order, OrderState.ASSIGNED))
                    {
                        await transaction.RollbackAsync();
                        return false;
                    }
                }

                var rider = await _dbContext.Riders.FindAsync(riderId);
                if (rider != null && rider.State != RiderState.BUSY)
                {
                    if (!await _stateMachine.TransitionRiderAsync(riderId, RiderState.BUSY))
                    {
                        await transaction.RollbackAsync();
                        return false;
                    }
                }

                await transaction.CommitAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Concurrency collision detected while Rider {RiderId} was accepting offer {OfferId}", 
                    riderId, offerId);
                return false; // Concurrency conflict handled safely
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Exception while Rider {RiderId} was accepting offer {OfferId}", riderId, offerId);
                return false;
            }

            // แจ้ง Admin Dashboard และ ร้านค้า ลูกค้า
            var orderNotifier = _serviceProvider.GetRequiredService<OrderNotificationService>();
            foreach (var order in orders)
            {
                await _adminNotifier.NotifyOrderAssignedAsync(order.Id, riderId, order.AssignedAt);
                await orderNotifier.NotifyOrderStatusChangedAsync(order, OrderState.OFFERING);
            }

            _logger.LogInformation(
                "Orders {OrderCount} assigned to Rider {RiderId} via offer {OfferId}",
                orders.Count, riderId, offerId);

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
    public async Task RejectOrTimeoutAsync(string offerId, string riderId)
    {
        var db = _redis.GetDatabase();
        var lockKey = $"lock:offer:{offerId}";

        var acquired = await db.StringSetAsync(lockKey, "locked", TimeSpan.FromSeconds(5), When.NotExists);
        if (!acquired)
        {
            _logger.LogWarning("Reject/Timeout skipped: offer {OfferId} is currently being processed by another action", offerId);
            return;
        }

        try
        {
            // ปลดล็อค Rider Lock
            await _lockService.ReleaseLockAsync(riderId, offerId);
            
            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                var rider = await _dbContext.Riders.FindAsync(riderId);
                if (rider != null && rider.State == RiderState.RESERVED)
                {
                    if (!await _stateMachine.TransitionRiderAsync(rider, RiderState.IDLE))
                    {
                        await transaction.RollbackAsync();
                        return;
                    }
                }

                // เปลี่ยน Order กลุ่มนี้กลับเป็น MATCHING → หาคนใหม่
                var orders = await _dbContext.Orders
                    .Where(o => o.CurrentOfferId == offerId && o.State == OrderState.OFFERING)
                    .OrderBy(o => o.BatchSequence)
                    .ToListAsync();
                    
                if (orders.Count == 0)
                {
                    // FIX: Commit เพื่อให้ Rider เปลี่ยนเป็น IDLE สำเร็จ ไม่ติดใน RESERVED state จาก Rollback
                    await transaction.CommitAsync();
                    return;
                }

                bool allTransitioned = true;
                foreach (var order in orders)
                {
                    if (order.BatchGroupId != null)
                    {
                        // Check if there are other active orders in the same batch not part of this offer
                        var otherActiveOrdersInBatch = await _dbContext.Orders
                            .AnyAsync(o => o.BatchGroupId == order.BatchGroupId 
                                        && o.Id != order.Id 
                                        && o.CurrentOfferId != offerId);
                        
                        if (otherActiveOrdersInBatch)
                        {
                            _logger.LogInformation("Detaching dynamically injected/split order {OrderId} from batch {BatchId} due to rejection/timeout", 
                                order.Id, order.BatchGroupId);
                            order.BatchGroupId = null;
                            order.BatchSequence = 0;
                        }
                    }

                    if (!await _stateMachine.TransitionOrderAsync(order, OrderState.MATCHING))
                    {
                        allTransitioned = false;
                        break;
                    }
                }

                if (!allTransitioned)
                {
                    await transaction.RollbackAsync();
                    return;
                }

                await transaction.CommitAsync();

                _logger.LogInformation(
                    "Orders ({Count}) re-dispatching after rejection/timeout from Rider {RiderId}",
                    orders.Count, riderId);

                // Re-dispatch: หาคนถัดไป — resolve DispatchService via DI เพื่อหลีกเลี่ยง circular dependency
                var dispatchService = _serviceProvider.GetRequiredService<DispatchService>();
                await dispatchService.FindAndOfferAsync(orders);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error occurred during RejectOrTimeoutAsync for offer {OfferId}", offerId);
            }
        }
        finally
        {
            await db.KeyDeleteAsync(lockKey);
        }
    }
}
