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
            var batchId = $"BATCH-{Guid.NewGuid():N}"[..16];
            
            sibling.BatchGroupId = batchId;
            sibling.BatchSequence = 1;
            
            targetOrder.BatchGroupId = batchId;
            targetOrder.BatchSequence = 2;

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Grouped order {TargetId} with {SiblingId} (Same-Shop). BatchId: {BatchId}", targetOrder.Id, sibling.Id, batchId);
            return batchId;
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
            
            // ใช้ OSRM /trip เพื่อจัดลำดับ Dropoff ที่ดีที่สุด
            // จุด 0: Pickup (ใช้จุดกึ่งกลางหรือใช้จุดของออเดอร์แรก)
            // จุด 1: Dropoff O
            // จุด 2: Dropoff Target
            var points = new List<(double Lat, double Lng)>
            {
                (o.PickupLocation.Y, o.PickupLocation.X),
                (o.DropoffLocation.Y, o.DropoffLocation.X),
                (targetOrder.DropoffLocation.Y, targetOrder.DropoffLocation.X)
            };

            var seq = await _routingService.GetOptimizedTripSequenceAsync(points);
            
            // seq[0] คือ Pickup, seq[1] คือ จุดที่ 1, seq[2] คือ จุดที่ 2
            if (seq.Count == 3)
            {
                // ถ้า seq[1] == 1 แปลว่า O ไปก่อน Target
                if (seq[1] == 1)
                {
                    o.BatchSequence = 1;
                    targetOrder.BatchSequence = 2;
                }
                else
                {
                    o.BatchSequence = 2;
                    targetOrder.BatchSequence = 1;
                }
            }
            else
            {
                // Fallback
                o.BatchSequence = 1;
                targetOrder.BatchSequence = 2;
            }

            targetOrder.BatchGroupId = batchId;
            await _dbContext.SaveChangesAsync();
            
            _logger.LogInformation("Grouped order {TargetId} with {SiblingId} (Same-Direction). BatchId: {BatchId}", targetOrder.Id, o.Id, batchId);
            return batchId;
        }

        return null;
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
