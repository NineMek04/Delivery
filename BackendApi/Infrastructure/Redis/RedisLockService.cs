using StackExchange.Redis;

namespace BackendApi.Infrastructure.Redis;

/// <summary>
/// Distributed Lock ด้วย Redis — ป้องกัน Race Condition ในการจอง Rider
/// ใช้ SETNX + Lua script เพื่อ Atomic check-and-set
/// 
/// กฎสำคัญ: Redis ไม่ใช่ Source of Truth — PostgreSQL คือ Source of Truth เสมอ
/// Redis ใช้เป็น Operational Cache สำหรับ real-time operations เท่านั้น
/// </summary>
public class RedisLockService
{
    private readonly IDatabase _db;
    private readonly ILogger<RedisLockService> _logger;

    // Lua script สำหรับ atomic release (ลบ key เฉพาะเมื่อ value ตรงกับ offerId)
    private const string ReleaseLockScript = @"
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end";

    public RedisLockService(IConnectionMultiplexer redis, ILogger<RedisLockService> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;
    }

    /// <summary>
    /// จองตัว Rider ชั่วคราว (SETNX + TTL)
    /// </summary>
    /// <param name="riderId">Rider ที่ต้องการจอง</param>
    /// <param name="offerId">Offer ID สำหรับ idempotency</param>
    /// <param name="timeout">ระยะเวลาจอง (default 30 วินาที)</param>
    /// <returns>true ถ้าจองสำเร็จ, false ถ้ามีคนอื่นจองอยู่แล้วหรือเชื่อมต่อ Redis ไม่ได้</returns>
    public async Task<bool> TryAcquireRiderLockAsync(string riderId, string offerId, TimeSpan timeout)
    {
        var key = RiderLockKey(riderId);
        try
        {
            var acquired = await _db.StringSetAsync(key, offerId, timeout, When.NotExists);

            if (acquired)
            {
                _logger.LogInformation(
                    "Lock acquired: Rider {RiderId} reserved by offer {OfferId} for {Timeout}s",
                    riderId, offerId, timeout.TotalSeconds);
            }
            else
            {
                var currentHolder = await _db.StringGetAsync(key);
                _logger.LogDebug(
                    "Lock failed: Rider {RiderId} already locked by {CurrentHolder}",
                    riderId, currentHolder);
            }

            return acquired;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Redis unavailable — Lock failed to acquire for Rider {RiderId} (offer {OfferId}) due to connection exception", riderId, offerId);
            return false;
        }
    }

    /// <summary>
    /// ปลดล็อค Rider — ตรวจสอบ offerId ก่อนลบเพื่อป้องกันลบ lock ของคนอื่น
    /// </summary>
    public async Task<bool> ReleaseLockAsync(string riderId, string offerId)
    {
        var key = RiderLockKey(riderId);
        try
        {
            var result = (int)await _db.ScriptEvaluateAsync(
                ReleaseLockScript,
                new RedisKey[] { key },
                new RedisValue[] { offerId });

            if (result == 1)
            {
                _logger.LogInformation("Lock released: Rider {RiderId} (offer {OfferId})", riderId, offerId);
            }
            else
            {
                _logger.LogWarning(
                    "Lock release failed: Rider {RiderId} — offer {OfferId} does not match current holder",
                    riderId, offerId);
            }

            return result == 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis unavailable — Failed to release lock for Rider {RiderId} (offer {OfferId}) due to connection exception", riderId, offerId);
            return false;
        }
    }

    /// <summary>
    /// ตรวจสอบว่า Rider ถูกจองอยู่หรือไม่
    /// </summary>
    public async Task<string?> GetLockHolderAsync(string riderId)
    {
        var key = RiderLockKey(riderId);
        try
        {
            var value = await _db.StringGetAsync(key);
            return value.HasValue ? value.ToString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — Failed to get lock holder for Rider {RiderId}", riderId);
            return null;
        }
    }

    /// <summary>
    /// ตรวจสอบว่า Rider ถูกจองอยู่หรือไม่
    /// </summary>
    public async Task<bool> IsLockedAsync(string riderId)
    {
        try
        {
            return await _db.KeyExistsAsync(RiderLockKey(riderId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — Failed to check lock status for Rider {RiderId}", riderId);
            return false;
        }
    }

    private static string RiderLockKey(string riderId) => $"dispatch:lock:rider:{riderId}";
}
