using System.Threading;
using BackendApi.Models.DTOs;

namespace BackendApi.Services.Telemetry;

/// <summary>
/// Singleton service that stores telemetry metrics in memory thread-safely.
/// Avoids heavy DB queries on high-frequency SignalR updates by utilizing lockless counters for GPS ticks
/// and fast memory locks for low-frequency stats snapshots.
/// </summary>
public class TelemetryAggregator
{
    private int _gpsTickCount = 0;
    
    private readonly object _snapshotLock = new();
    private int _activeRidersCount = 0;
    private int _dispatchQueueSize = 0;
    private int _ridersBusyCount = 0;
    private int _ridersIdleCount = 0;
    private int _ridersOfflineCount = 0;
    private double _averageDeliveriesPerRider = 0.0;

    /// <summary>
    /// Fast lockless increment for high-frequency GPS location updates.
    /// </summary>
    public void IncrementGpsTick()
    {
        Interlocked.Increment(ref _gpsTickCount);
    }

    /// <summary>
    /// Drains the accumulated GPS tick count and resets it to 0.
    /// </summary>
    public int DrainGpsTickCount()
    {
        return Interlocked.Exchange(ref _gpsTickCount, 0);
    }

    /// <summary>
    /// Periodically updates operational metrics snapshotted from PostgreSQL.
    /// </summary>
    public void UpdateSnapshot(
        int activeRidersCount,
        int dispatchQueueSize,
        int ridersBusyCount,
        int ridersIdleCount,
        int ridersOfflineCount,
        double averageDeliveriesPerRider)
    {
        lock (_snapshotLock)
        {
            _activeRidersCount = activeRidersCount;
            _dispatchQueueSize = dispatchQueueSize;
            _ridersBusyCount = ridersBusyCount;
            _ridersIdleCount = ridersIdleCount;
            _ridersOfflineCount = ridersOfflineCount;
            _averageDeliveriesPerRider = averageDeliveriesPerRider;
        }
    }

    /// <summary>
    /// Computes and returns the latest high-frequency telemetry metrics.
    /// Resets the GPS counter based on the measurement window size.
    /// </summary>
    public RealtimeTelemetryDto GetTelemetry(double windowSeconds)
    {
        var rawGpsTicks = DrainGpsTickCount();

        lock (_snapshotLock)
        {
            // If there are no active riders in the system, enforce GPS updates/sec as 0.0
            double gpsPerSec = 0.0;
            if (_activeRidersCount > 0 && windowSeconds > 0)
            {
                gpsPerSec = rawGpsTicks / windowSeconds;
            }

            return new RealtimeTelemetryDto
            {
                ActiveRidersCount = _activeRidersCount,
                GpsUpdatesPerSecond = Math.Round(gpsPerSec, 1), // Rounded to 1 decimal place to reduce jitter
                DispatchQueueSize = _dispatchQueueSize
            };
        }
    }

    /// <summary>
    /// Returns the cached rider utilization metrics.
    /// </summary>
    public RiderUtilizationDto GetUtilization()
    {
        lock (_snapshotLock)
        {
            return new RiderUtilizationDto
            {
                RidersBusyCount = _ridersBusyCount,
                RidersIdleCount = _ridersIdleCount,
                RidersOfflineCount = _ridersOfflineCount,
                AverageDeliveriesPerRider = _averageDeliveriesPerRider
            };
        }
    }
}
