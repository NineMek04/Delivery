using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendApi.Data;
using BackendApi.Hubs;
using BackendApi.Hubs.Chat;
using BackendApi.Hubs.Tracking;
using BackendApi.Infrastructure.Redis;
using BackendApi.Services.Ai;
using BackendApi.Services.Tracking;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using BackendApi.Core.StateMachines;
using BackendApi.Features.FleetTracking.Telemetry;
using BackendApi.Features.FleetTracking.Models;
using BackendApi.Security;
using BackendApi.Security.Models;
using BackendApi.Security.Services;

namespace BackendApi.Services.Telemetry
{
    /// <summary>
    /// Telemetry Service — จัดการการประมวลผลพิกัดความถี่สูงอย่างสมบูรณ์แบบ
    /// คอยทำ Snap-to-Road (OSRM), จัดเก็บบน Redis cache เท่านั้นใน Hot path,
    /// และทำการ Broadcast พิกัดผ่าน SignalR แบบ Dynamic Throttling
    /// </summary>
    public partial class TelemetryService
    {
        private async Task BroadcastAdminLocationAsync(
            string riderId,
            double lat,
            double lng,
            double accuracy,
            DateTime timestamp,
            bool isSnapped)
        {
            var database = _redis.GetDatabase();
            var riderState = await GetRiderStateAsync(database, riderId);

            await _hubContext.Clients.Group(AdminGroup).SendAsync(
                "RiderLocationUpdated",
                new
                {
                    RiderId = riderId,
                    Lat = lat,
                    Lng = lng,
                    Accuracy = accuracy,
                    State = riderState,
                    Timestamp = timestamp,
                    isSnapped
                });
        }

        private async Task<string> GetRiderStateAsync(
            IDatabase database,
            string riderId)
        {
            var statusCacheKey = $"riders:status:{riderId}";
            try
            {
                var cachedState = await database.StringGetAsync(statusCacheKey);
                if (cachedState.HasValue)
                {
                    return cachedState.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to read rider state cache for Rider {RiderId}; falling back to PostgreSQL",
                    riderId);
            }

            var riderState = await _dbContext.Riders
                .AsNoTracking()
                .Where(rider => rider.Id == riderId)
                .Select(rider => (RiderState?)rider.State)
                .FirstOrDefaultAsync();
            if (riderState is null)
            {
                return RiderState.OFFLINE.ToString();
            }

            var state = riderState.Value.ToString();
            try
            {
                await database.StringSetAsync(
                    statusCacheKey,
                    state,
                    TimeSpan.FromMinutes(5));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to cache rider state for Rider {RiderId}",
                    riderId);
            }
            return state;
        }

        private async Task<IReadOnlyCollection<string>> GetActiveCustomerIdsAsync(
            IDatabase database,
            string riderId)
        {
            try
            {
                var cachedEntries = await database.HashGetAllAsync(
                    ActiveOrderRecipientCache.GetKey(riderId));
                if (ActiveOrderRecipientCache.TryGetCustomerIds(
                    cachedEntries,
                    out var cachedCustomerIds))
                {
                    return cachedCustomerIds;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to read active order recipient cache for Rider {RiderId}; falling back to PostgreSQL",
                    riderId);
            }

            var activeOrders = await _dbContext.Orders
                .AsNoTracking()
                .Where(order =>
                    order.AssignedRiderId == riderId &&
                    (order.State == OrderState.ASSIGNED ||
                     order.State == OrderState.PICKING_UP ||
                     order.State == OrderState.DELIVERING))
                .Select(order => new
                {
                    order.Id,
                    order.CustomerId
                })
                .ToListAsync();

            var customerIds = activeOrders
                .Select(order => order.CustomerId)
                .Where(customerId => !string.IsNullOrWhiteSpace(customerId))
                .Select(customerId => customerId!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            try
            {
                await ActiveOrderRecipientCache.ReplaceAsync(
                    database,
                    riderId,
                    activeOrders.Select(order =>
                        new KeyValuePair<string, string?>(
                            order.Id,
                            order.CustomerId)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to refresh active order recipient cache for Rider {RiderId}",
                    riderId);
            }

            return customerIds;
        }

        /// <summary>
        /// ประมวลผลการ Snap พิกัดและดึงรายละเอียดเส้นทางในแบบ Asynchronous/Event-driven จาก Background Worker
        /// </summary>
        public async Task ProcessSnapAndBroadcastAsync(TrackPoint point)
        {
            var riderId = point.RiderId;
            var lat = point.Lat;
            var lng = point.Lng;
            var now = point.Timestamp;

            using var logScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["CorrelationId"] = Guid.NewGuid().ToString(), // background task correlation
                ["OrderId"] = null,
                ["RiderId"] = riderId
            });

            double snappedLat = lat;
            double snappedLng = lng;
            bool isSnapped = false;
            string? snappedPolyline = null;

            // 1. Call OSRM Snap-to-Road
            try
            {
                var snappedResult = await _routingService.SnapToRoadAsync(lat, lng);
                snappedLat = snappedResult.Lat;
                snappedLng = snappedResult.Lng;
                isSnapped = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to snap coordinate ({Lat}, {Lng}) in worker for Rider {RiderId}", lat, lng, riderId);
            }

            var db = _redis.GetDatabase();

            // Save snapped coordinate to Redis Cache
            if (isSnapped)
            {
                try
                {
                    var snappedGpsKey = $"riders:snapped_gps:{riderId}";
                    await db.HashSetAsync(snappedGpsKey, new[]
                    {
                        new HashEntry("lat", snappedLat),
                        new HashEntry("lng", snappedLng),
                        new HashEntry("updated_at", DateTime.UtcNow.Ticks)
                    });
                    await db.KeyExpireAsync(snappedGpsKey, TimeSpan.FromHours(24));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cache snapped coordinates to Redis in worker for Rider {RiderId}", riderId);
                }
            }

            double? routeDistance = null;
            double? routeDuration = null;

            // 2. Resolve Snapped Polyline for active order if any
            try
            {
                var activeOrder = await _dbContext.Orders
                    .AsNoTracking()
                    .Where(o => o.AssignedRiderId == riderId && 
                                (o.State == OrderState.ASSIGNED || 
                                 o.State == OrderState.PICKING_UP || 
                                 o.State == OrderState.DELIVERING))
                    .Select(o => new { o.Id, o.State, o.PickupLocation, o.DropoffLocation })
                    .FirstOrDefaultAsync();

                if (activeOrder != null)
                {
                    double? destLat = null;
                    double? destLng = null;
                    var orderId = activeOrder.Id;
                    var status = activeOrder.State.ToString();

                    if (activeOrder.State == OrderState.ASSIGNED || activeOrder.State == OrderState.PICKING_UP)
                    {
                        destLat = activeOrder.PickupLocation?.Y;
                        destLng = activeOrder.PickupLocation?.X;
                    }
                    else if (activeOrder.State == OrderState.DELIVERING)
                    {
                        destLat = activeOrder.DropoffLocation?.Y;
                        destLng = activeOrder.DropoffLocation?.X;
                    }

                    _logger.LogInformation(
                        "Route Debug | Rider:{RiderId} Order:{OrderId} Status:{Status} Start:{Lat},{Lng} Dest:{DestLat},{DestLng}",
                        riderId,
                        orderId,
                        status,
                        snappedLat,
                        snappedLng,
                        destLat,
                        destLng
                    );

                    if (destLat.HasValue && destLng.HasValue)
                    {
                        var routeResult = await _routingService.GetRouteDetailsAsync(snappedLat, snappedLng, destLat.Value, destLng.Value);
                        snappedPolyline = routeResult.Polyline;
                        routeDistance = routeResult.DistanceMeters;
                        routeDuration = routeResult.DurationSeconds;

                        _logger.LogInformation(
                            "Route Result Debug | DistanceMeters:{DistanceMeters} DurationSeconds:{DurationSeconds} EncodedPolyline:{EncodedPolyline}",
                            routeDistance,
                            routeDuration,
                            snappedPolyline
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve snapped polyline in worker for Rider {RiderId}", riderId);
            }

            // 3. Broadcast Snapped Location and Path via SignalR to Admin & Customers
            var riderState = await GetRiderStateAsync(db, riderId);

            // A. Broadcast Snapped to Admin Group
            await _hubContext.Clients.Group(AdminGroup).SendAsync("RiderLocationUpdated", new
            {
                RiderId = riderId,
                Lat = snappedLat,
                Lng = snappedLng,
                Accuracy = 5.0, // default/simulated accuracy for snapped coordinates
                State = riderState,
                Timestamp = now,
                isSnapped = isSnapped,
                snappedPolyline = snappedPolyline,
                routeDistance = routeDistance,
                routeDuration = routeDuration
            });

            // B. Broadcast Snapped to Customers (แหล่งเดียวที่ลูกค้าได้รับพิกัด — ป้องกัน marker กระโดดจาก Raw+Snapped)
            var customerIds = await GetActiveCustomerIdsAsync(db, riderId);
            foreach (var customerId in customerIds)
            {
                await _hubContext.Clients.Group($"customer:{customerId}").SendAsync("RiderLocationUpdated", new
                {
                    RiderId = riderId,
                    Lat = snappedLat,
                    Lng = snappedLng,
                    Accuracy = 5.0,
                    State = riderState,
                    Timestamp = now,
                    isSnapped = isSnapped,
                    snappedPolyline = snappedPolyline,
                    routeDistance = routeDistance,
                    routeDuration = routeDuration
                });
            }

            // C. Broadcast Snapped to Rider's own group (ไรเดอร์ได้รับ snappedPolyline เพื่ออัปเดตเส้นทางแบบเรียลไทม์)
            await _hubContext.Clients.Group($"rider:{riderId}").SendAsync("RiderLocationUpdated", new
            {
                RiderId = riderId,
                Lat = snappedLat,
                Lng = snappedLng,
                Accuracy = 5.0,
                State = riderState,
                Timestamp = now,
                isSnapped = isSnapped,
                snappedPolyline = snappedPolyline,
                routeDistance = routeDistance,
                routeDuration = routeDuration
            });

            // D. Broadcast RiderLocationSnapped to admins (backward compatibility)
            await _hubContext.Clients.Group(AdminGroup).SendAsync("RiderLocationSnapped", new
            {
                RiderId = riderId,
                Lat = snappedLat,
                Lng = snappedLng,
                Timestamp = now,
                isSnapped = true
            });
        }

    }
}
