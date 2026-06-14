using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Services.Ai;
using BackendApi.Core.Helpers;
using BackendApi.Core.StateMachines;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BackendApi.Services.Dispatch;

public class BatchEvaluator
{
    private readonly ApplicationDbContext _dbContext;
    private readonly OsrmRoutingService _routingService;
    private readonly ILogger<BatchEvaluator> _logger;

    public BatchEvaluator(ApplicationDbContext dbContext, OsrmRoutingService routingService, ILogger<BatchEvaluator> logger)
    {
        _dbContext = dbContext;
        _routingService = routingService;
        _logger = logger;
    }

    /// <summary>
    /// สแกนหาออเดอร์ในระบบที่เหมาะสมจะจัดเป็นกลุ่มพ่วงกับออเดอร์เป้าหมาย
    /// (สำหรับ Pre-dispatch Batching)
    /// </summary>
    public async Task<string?> TryGroupAsync(Order targetOrder)
    {
        if (targetOrder.BatchGroupId != null) return targetOrder.BatchGroupId;

        // ดึงออเดอร์ทั้งหมดที่ว่าง (CREATED หรือ MATCHING) และยังไม่ถูกผูก batch เกิน 2 (เราจะเพิ่มเป้าหมายเข้าไปเป็นคนที่ 2 หรือ 3)
        // หรือดึงออเดอร์เดี่ยวเพื่อมาตั้ง batch group ใหม่
        
        var eligibleOrders = await _dbContext.Orders
            .Where(o => (o.State == OrderState.CREATED || o.State == OrderState.MATCHING) 
                     && o.Id != targetOrder.Id
                     && o.PickupLocation != null 
                     && o.DropoffLocation != null)
            .ToListAsync();

        // 1. Same-Shop Grouping
        var sameShopOrders = eligibleOrders
            .Where(o => o.ShopId == targetOrder.ShopId && o.BatchGroupId == null) // หาที่ยังไม่จัดกลุ่ม
            .OrderBy(o => o.CreatedAt)
            .ToList();

        if (sameShopOrders.Any())
        {
            var sibling = sameShopOrders.First();

            // [DOUBLE-BATCH FIX] Re-fetch sibling inside a fresh tracked read so that
            // EF xmin concurrency token (RowVersion) catches concurrent modifications.
            // If another request already claimed this sibling, BatchGroupId will be non-null
            // and we fall through to Same-Direction grouping.
            var siblingFresh = await _dbContext.Orders.FindAsync(sibling.Id);
            if (siblingFresh != null && siblingFresh.BatchGroupId == null)
            {
                var batchId = $"BATCH-{Guid.NewGuid():N}"[..16];
                
                siblingFresh.BatchGroupId = batchId;
                siblingFresh.BatchSequence = 1;
                
                targetOrder.BatchGroupId = batchId;
                targetOrder.BatchSequence = 2;

                try
                {
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("Grouped order {TargetId} with {SiblingId} (Same-Shop). BatchId: {BatchId}", targetOrder.Id, sibling.Id, batchId);
                    return batchId;
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Another request claimed sibling concurrently — fall through to direction grouping
                    _logger.LogWarning("Batch grouping concurrency conflict for sibling {SiblingId}. Falling through to Same-Direction.", sibling.Id);
                    targetOrder.BatchGroupId = null;
                    targetOrder.BatchSequence = 0;
                    _dbContext.Entry(siblingFresh).Reload();
                }
            }
        }

        // 2. Same-Direction Grouping (Dropoff <= 5km)
        // ถ้าเป็นออเดอร์เดี่ยว หาคนอื่นที่ไม่ได้อยู่ร้านเดียวกันแต่ไปทางเดียวกัน
        if (targetOrder.PickupLocation == null || targetOrder.DropoffLocation == null) return null;

        var targetBearing = CalculateBearing(targetOrder.PickupLocation.Y, targetOrder.PickupLocation.X, targetOrder.DropoffLocation.Y, targetOrder.DropoffLocation.X);

        foreach (var o in eligibleOrders.Where(x => x.BatchGroupId == null))
        {
            if (o.PickupLocation == null || o.DropoffLocation == null) continue;

            // เงื่อนไข: Pickup ห่างไม่เกิน 1.5km
            var pickupDistKm = CalculateDistanceKm(targetOrder.PickupLocation.Y, targetOrder.PickupLocation.X, o.PickupLocation.Y, o.PickupLocation.X);
            if (pickupDistKm > 1.5) continue;

            // เงื่อนไข: Dropoff ห่างไม่เกิน 5km (แก้ไขจาก 2km ตาม feedback)
            var dropoffDistKm = CalculateDistanceKm(targetOrder.DropoffLocation.Y, targetOrder.DropoffLocation.X, o.DropoffLocation.Y, o.DropoffLocation.X);
            if (dropoffDistKm > 5.0) continue;

            // เงื่อนไข: ทิศทาง (Bearing) ต่างกันไม่เกิน 45 องศา
            var oBearing = CalculateBearing(o.PickupLocation.Y, o.PickupLocation.X, o.DropoffLocation.Y, o.DropoffLocation.X);
            if (!IsSameDirection(targetBearing, oBearing, 45.0)) continue;

            // ผ่านเงื่อนไขหมด จัดกลุ่ม
            var batchId = $"BATCH-{Guid.NewGuid():N}"[..16];
            o.BatchGroupId = batchId;
            
            // 4 points for Same-Direction grouping:
            // Point 0: Sibling Pickup
            // Point 1: Target Pickup
            // Point 2: Sibling Dropoff
            // Point 3: Target Dropoff
            var points = new List<(double Lat, double Lng)>
            {
                (o.PickupLocation.Y, o.PickupLocation.X),
                (targetOrder.PickupLocation.Y, targetOrder.PickupLocation.X),
                (o.DropoffLocation.Y, o.DropoffLocation.X),
                (targetOrder.DropoffLocation.Y, targetOrder.DropoffLocation.X)
            };

            var seq = await _routingService.GetOptimizedTripSequenceAsync(points);
            ApplyDropoffVisitOrder(o, targetOrder, seq);

            targetOrder.BatchGroupId = batchId;
            await _dbContext.SaveChangesAsync();
            
            _logger.LogInformation("Grouped order {TargetId} with {SiblingId} (Same-Direction). BatchId: {BatchId}", targetOrder.Id, o.Id, batchId);
            return batchId;
        }

        return null;
    }

    internal static void ApplyDropoffVisitOrder(
        Order siblingOrder,
        Order targetOrder,
        IReadOnlyList<int> waypointVisitOrder)
    {
        // OSRM returns one waypoint_index per input coordinate. The value at each
        // input index is that coordinate's visit position in the optimized trip.
        if (waypointVisitOrder.Count == 4 &&
            waypointVisitOrder[2] >= 0 &&
            waypointVisitOrder[3] >= 0 &&
            waypointVisitOrder[2] != waypointVisitOrder[3])
        {
            var siblingFirst = waypointVisitOrder[2] < waypointVisitOrder[3];
            siblingOrder.BatchSequence = siblingFirst ? 1 : 2;
            targetOrder.BatchSequence = siblingFirst ? 2 : 1;
            return;
        }

        siblingOrder.BatchSequence = 1;
        targetOrder.BatchSequence = 2;
    }

    private double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
    {
        var dLon = ToRad(lon2 - lon1);
        var y = Math.Sin(dLon) * Math.Cos(ToRad(lat2));
        var x = Math.Cos(ToRad(lat1)) * Math.Sin(ToRad(lat2)) - Math.Sin(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Cos(dLon);
        var brng = Math.Atan2(y, x);
        return (ToDeg(brng) + 360) % 360;
    }

    private double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return 6371 * c;
    }

    private double ToRad(double degrees) => degrees * (Math.PI / 180);
    private double ToDeg(double radians) => radians * (180 / Math.PI);

    private bool IsSameDirection(double bearing1, double bearing2, double toleranceDegrees)
    {
        var diff = Math.Abs(bearing1 - bearing2);
        if (diff > 180) diff = 360 - diff;
        return diff <= toleranceDegrees;
    }
}
