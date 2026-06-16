using StackExchange.Redis;
using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using Microsoft.EntityFrameworkCore;

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
    private readonly IDatabase? _db;
    private readonly ILogger<RedisLockService> _logger;
    private readonly ApplicationDbContext? _dbContext;

    // Lua script สำหรับ atomic release (ลบ key เฉพาะเมื่อ value ตรงกับ offerId)
    private const string ReleaseLockScript = @"
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end";

    public RedisLockService(IConnectionMultiplexer redis, ILogger<RedisLockService> logger)
    {
        try
        {
            _db = redis.GetDatabase();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis connection failed. Lock service will run in fallback mode.");
        }
        _logger = logger;
    }

    public RedisLockService(IConnectionMultiplexer redis, ILogger<RedisLockService> logger, ApplicationDbContext dbContext)
        : this(redis, logger)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// จองตัว Rider ชั่วคราว (SETNX + TTL)
    /// </summary>
    /// <param name="riderId">Rider ที่ต้องการจอง</param>
    /// <param name="offerId">Offer ID สำหรับ idempotency</param>
    /// <param name="timeout">ระยะเวลาจอง (default 30 วินาที)</param>
    /// <returns>true ถ้าจองสำเร็จ, false ถ้ามีคนอื่นจองอยู่แล้วหรือเชื่อมต่อ Redis ไม่ได้</returns>
    public virtual async Task<bool> TryAcquireRiderLockAsync(string riderId, string offerId, TimeSpan timeout)
    {
        var key = RiderLockKey(riderId);
        try
        {
            if (_db == null)
            {
                throw new Exception("Redis is not initialized");
            }

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
            _logger.LogWarning(ex, "Redis unavailable — Falling back to PostgreSQL for lock acquisition for Rider {RiderId} (offer {OfferId})", riderId, offerId);
            return await FallbackAcquireLockAsync(key, offerId, timeout);
        }
    }

    /// <summary>
    /// ปลดล็อค Rider — ตรวจสอบ offerId ก่อนลบเพื่อป้องกันลบ lock ของคนอื่น
    /// </summary>
    public virtual async Task<bool> ReleaseLockAsync(string riderId, string offerId)
    {
        var key = RiderLockKey(riderId);
        try
        {
            if (_db == null)
            {
                throw new Exception("Redis is not initialized");
            }

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
            _logger.LogWarning(ex, "Redis unavailable — Falling back to PostgreSQL for lock release for Rider {RiderId} (offer {OfferId})", riderId, offerId);
            return await FallbackReleaseLockAsync(key, offerId);
        }
    }

    /// <summary>
    /// ตรวจสอบว่า Rider ถูกจองอยู่หรือไม่
    /// </summary>
    public virtual async Task<string?> GetLockHolderAsync(string riderId)
    {
        var key = RiderLockKey(riderId);
        try
        {
            if (_db == null)
            {
                throw new Exception("Redis is not initialized");
            }
            var value = await _db.StringGetAsync(key);
            return value.HasValue ? value.ToString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — Falling back to PostgreSQL to get lock holder for Rider {RiderId}", riderId);
            return await FallbackGetLockHolderAsync(key);
        }
    }

    /// <summary>
    /// ตรวจสอบว่า Rider ถูกจองอยู่หรือไม่
    /// </summary>
    public virtual async Task<bool> IsLockedAsync(string riderId)
    {
        var key = RiderLockKey(riderId);
        try
        {
            if (_db == null)
            {
                throw new Exception("Redis is not initialized");
            }
            return await _db.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — Falling back to PostgreSQL to check lock status for Rider {RiderId}", riderId);
            return await FallbackIsLockedAsync(key);
        }
    }

    private async Task<bool> FallbackAcquireLockAsync(string key, string offerId, TimeSpan timeout)
    {
        if (_dbContext == null)
        {
            _logger.LogWarning("PostgreSQL DbContext is not configured for fallback lock.");
            return false;
        }

        try
        {
            var now = DateTime.UtcNow;
            var expiresAt = now.Add(timeout);

            // Handle InMemory DB provider for unit testing suite support
            if (_dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                var existing = await _dbContext.DistributedLocks.FindAsync(key);
                if (existing == null)
                {
                    var newLock = new DistributedLock { LockKey = key, Value = offerId, ExpiresAt = expiresAt };
                    await _dbContext.DistributedLocks.AddAsync(newLock);
                    await _dbContext.SaveChangesAsync();
                    return true;
                }
                else if (existing.ExpiresAt <= now || existing.Value == offerId)
                {
                    existing.Value = offerId;
                    existing.ExpiresAt = expiresAt;
                    await _dbContext.SaveChangesAsync();
                    return true;
                }
                return false;
            }

            // atomic insert or update if expired/same value
            var sql = @"
                INSERT INTO ""DistributedLocks"" (""LockKey"", ""Value"", ""ExpiresAt"")
                VALUES ({0}, {1}, {2})
                ON CONFLICT (""LockKey"")
                DO UPDATE SET 
                    ""Value"" = EXCLUDED.""Value"",
                    ""ExpiresAt"" = EXCLUDED.""ExpiresAt""
                WHERE ""DistributedLocks"".""ExpiresAt"" <= {3}
                   OR ""DistributedLocks"".""Value"" = EXCLUDED.""Value""";

            var affected = await _dbContext.Database.ExecuteSqlRawAsync(sql, key, offerId, expiresAt, now);
            var acquired = affected > 0;

            if (acquired)
            {
                _logger.LogInformation(
                    "Fallback Lock acquired on PG: Rider Lock {LockKey} reserved by offer {OfferId} for {Timeout}s",
                    key, offerId, timeout.TotalSeconds);
            }
            else
            {
                _logger.LogDebug(
                    "Fallback Lock failed on PG: Rider Lock {LockKey} already locked by another active offer",
                    key);
            }

            return acquired;
        }
        catch (Exception dbEx)
        {
            _logger.LogCritical(dbEx, "PostgreSQL Lock Fallback failed for Lock {LockKey}", key);
            return false;
        }
    }

    private async Task<bool> FallbackReleaseLockAsync(string key, string offerId)
    {
        if (_dbContext == null)
        {
            _logger.LogWarning("PostgreSQL DbContext is not configured for fallback lock.");
            return false;
        }

        try
        {
            // Handle InMemory DB provider for unit testing suite support
            if (_dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                var existing = await _dbContext.DistributedLocks.FindAsync(key);
                if (existing != null && existing.Value == offerId)
                {
                    _dbContext.DistributedLocks.Remove(existing);
                    await _dbContext.SaveChangesAsync();
                    return true;
                }
                return false;
            }

            var sql = @"
                DELETE FROM ""DistributedLocks""
                WHERE ""LockKey"" = {0} AND ""Value"" = {1}";

            var affected = await _dbContext.Database.ExecuteSqlRawAsync(sql, key, offerId);
            var released = affected > 0;

            if (released)
            {
                _logger.LogInformation("Fallback Lock released on PG: Lock {LockKey} (offer {OfferId})", key, offerId);
            }
            else
            {
                _logger.LogWarning(
                    "Fallback Lock release failed on PG: Lock {LockKey} — offer {OfferId} does not match holder or does not exist",
                    key, offerId);
            }

            return released;
        }
        catch (Exception dbEx)
        {
            _logger.LogError(dbEx, "PostgreSQL Lock Fallback release failed for Lock {LockKey}", key);
            return false;
        }
    }

    private async Task<string?> FallbackGetLockHolderAsync(string key)
    {
        if (_dbContext == null)
        {
            _logger.LogWarning("PostgreSQL DbContext is not configured for fallback lock.");
            return null;
        }

        try
        {
            var now = DateTime.UtcNow;
            var lockItem = await _dbContext.DistributedLocks
                .Where(dl => dl.LockKey == key && dl.ExpiresAt > now)
                .Select(dl => dl.Value)
                .FirstOrDefaultAsync();

            return lockItem;
        }
        catch (Exception dbEx)
        {
            _logger.LogWarning(dbEx, "PostgreSQL Lock Fallback get holder failed for Lock {LockKey}", key);
            return null;
        }
    }

    private async Task<bool> FallbackIsLockedAsync(string key)
    {
        if (_dbContext == null)
        {
            _logger.LogWarning("PostgreSQL DbContext is not configured for fallback lock.");
            return false;
        }

        try
        {
            var now = DateTime.UtcNow;
            return await _dbContext.DistributedLocks
                .AnyAsync(dl => dl.LockKey == key && dl.ExpiresAt > now);
        }
        catch (Exception dbEx)
        {
            _logger.LogWarning(dbEx, "PostgreSQL Lock Fallback is locked check failed for Lock {LockKey}", key);
            return false;
        }
    }

    private static string RiderLockKey(string riderId) => $"dispatch:lock:rider:{riderId}";
}


