using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendApi.Data;
using BackendApi.Features.FleetTracking.Telemetry;
using BackendApi.Models;
using Microsoft.Extensions.Logging;

namespace BackendApi.Services.Telemetry
{
    /// <summary>
    /// Service สำหรับจัดการบันทึกพิกัดประวัติเดินทาง (GPS History Ledger) ลง PostGIS
    /// </summary>
    public class GpsHistoryService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<GpsHistoryService> _logger;

        public GpsHistoryService(
            ApplicationDbContext dbContext,
            ILogger<GpsHistoryService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// บันทึกรายการ TrackPoint ทั้งหมดลงฐานข้อมูล PostgreSQL/PostGIS
        /// </summary>
        public virtual async Task SavePointsAsync(List<TrackPoint> points, CancellationToken ct = default)
        {
            if (points == null || points.Count == 0)
                return;

            try
            {
                // ใช้ GeometryFactory force 2D เพื่อป้องกัน "Geometry has Z dimension but column does not"
                var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

                var entities = points.Select(p => new RiderLocationHistory
                {
                    RiderId = p.RiderId,
                    Location = factory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(p.Lng, p.Lat)),
                    RecordedAt = p.Timestamp
                }).ToList();

                _dbContext.RiderLocationHistories.AddRange(entities);
                var inserted = await _dbContext.SaveChangesAsync(ct);

                _logger.LogInformation("Successfully saved {Count} GPS points to PostGIS tracking ledger.", inserted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to bulk insert {Count} GPS points into database.", points.Count);
                throw;
            }
        }
    }
}
