using BackendApi.Data;
using BackendApi.Infrastructure.Redis;
using BackendApi.Models;
using NetTopologySuite.Geometries;

namespace BackendApi.Services.BackgroundWorkers;

/// <summary>
/// Background Service ทำหน้าที่ดึงข้อมูล GPS ที่ Buffer ไว้ใน GpsSyncBuffer
/// แล้วทำ Bulk Insert ลง PostGIS เป็นระยะ (The Ledger) เพื่อทำ Historical Tracking
/// </summary>
public class GpsSyncWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly GpsSyncBuffer _buffer;
    private readonly IConfiguration _config;
    private readonly ILogger<GpsSyncWorker> _logger;

    public GpsSyncWorker(
        IServiceProvider serviceProvider,
        GpsSyncBuffer buffer,
        IConfiguration config,
        ILogger<GpsSyncWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _buffer = buffer;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GpsSyncWorker started");

        var interval = TimeSpan.FromSeconds(_config.GetValue("Dispatch:GpsSyncIntervalSeconds", 30));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await SyncGpsToDatabaseAsync(stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // หยุดการทำงาน
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GpsSyncWorker");
            }
        }

        // ก่อนหยุดการทำงาน (Shutdown) บังคับ Flush ข้อมูลที่ค้างอยู่ลง DB
        _logger.LogInformation("GpsSyncWorker stopping. Flushing remaining buffer...");
        await SyncGpsToDatabaseAsync(CancellationToken.None);
    }

    /// <summary>
    /// สั่ง Flush buffer ทั้งหมดและ Insert ลง DB
    /// สามารถเรียกจากภายนอกได้ด้วยถ้าต้องการ (เช่น เมื่อ Rider Offline ทันที)
    /// </summary>
    public async Task SyncGpsToDatabaseAsync(CancellationToken ct)
    {
        var pointsToSync = _buffer.FlushAll();

        if (pointsToSync.Count == 0)
            return;

        await SavePointsToDatabaseAsync(pointsToSync, ct);
    }

    /// <summary>
    /// Save batch เฉพาะของ Rider คนเดียว (ใช้ตอน Rider เปลี่ยน State หรือ Logout)
    /// </summary>
    public async Task SyncRiderGpsToDatabaseAsync(string riderId, CancellationToken ct)
    {
        var pointsToSync = _buffer.ForceFlush(riderId);

        if (pointsToSync.Count == 0)
            return;

        await SavePointsToDatabaseAsync(pointsToSync, ct);
    }

    private async Task SavePointsToDatabaseAsync(List<TrackPoint> points, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var entities = points.Select(p => new RiderLocationHistory
        {
            RiderId = p.RiderId,
            Location = new Point(p.Lng, p.Lat) { SRID = 4326 },
            RecordedAt = p.Timestamp
        });

        await dbContext.RiderLocationHistories.AddRangeAsync(entities, ct);
        var inserted = await dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Bulk inserted {Count} GPS points into PostGIS", inserted);
    }
}
