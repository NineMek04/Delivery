using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Infrastructure.Redis;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using BackendApi.Security.Models;
using BackendApi.Security.Services;
using BackendApi.Services.Ai;
using BackendApi.Services.Telemetry;
using Microsoft.EntityFrameworkCore;
using Order = BackendApi.Models.Entities.Order;

namespace BackendApi.Services.Dispatch;

/// <summary>
/// Dispatch Orchestrator — คุม Flow การจับคู่ Rider กับ Order ทั้งหมด (The Heart)
/// 
/// Flow 30 วินาที:
/// 1. Order ใหม่ → เปลี่ยนเป็น MATCHING
/// 2. ดึง Nearby Idle Riders จาก Redis GEORADIUS
/// 3. ส่ง Candidates ให้ระบบจัดอันดับตามความเหมาะสม (async, ไม่ block SignalR)
/// 4. จอง Rider อันดับ 1 (Redis SETNX)
/// 5. ยิง Offer ผ่าน SignalR + OfferId + Version
/// 6. Accept → ASSIGNED / Timeout → Re-dispatch
/// 
/// Delegates:
///   - Heuristic Ranking → DispatchCandidateRanker
///   - Rider SignalR/FCM → DispatchRiderNotifier
///   - Admin SignalR     → DispatchAdminNotifier
///   - Rider Actions     → DispatchOfferHandler (Accept/Reject/Timeout)
/// </summary>
public partial class DispatchService
{
    /// <summary>
    /// ค้นหา Rider ที่ใกล้ที่สุดและยิง Offer ไปให้
    /// </summary>
    public virtual async Task FindAndOfferAsync(List<Order> orders)
    {
        if (orders == null || orders.Count == 0) return;

        // 1. ตรวจสอบและกรองออเดอร์ที่แสกนเกิน 3 ครั้งแล้วเพื่อเปลี่ยนสถานะเป็น CANCELLED
        var ordersToCancel = new List<Order>();
        var ordersToScan = new List<Order>();

        foreach (var order in orders)
        {
            if (order.DispatchAttempts >= 3)
            {
                ordersToCancel.Add(order);
            }
            else
            {
                order.DispatchAttempts++;
                ordersToScan.Add(order);
            }
        }

        if (ordersToCancel.Count > 0)
        {
            foreach (var order in ordersToCancel)
            {
                _logger.LogWarning("Order {OrderId} has exceeded maximum dispatch scan attempts (3). Cancelling order.", order.Id);
                OperationalMetrics.DispatchMatchesTotal.WithLabels(order.DispatchAttempts.ToString(), "cancelled").Inc();
                await _stateMachine.TransitionOrderAsync(order, OrderState.CANCELLED);
            }
            await _dbContext.SaveChangesAsync();
        }

        if (ordersToScan.Count == 0)
        {
            return; // ไม่มีออเดอร์เหลือให้ค้นหาไรเดอร์
        }

        orders = ordersToScan;
        var firstOrder = orders.First();
        using var scope = BeginLogScope(firstOrder.Id, firstOrder.AssignedRiderId);

        // บันทึกจำนวนครั้งที่สแกนที่เพิ่มขึ้นของออเดอร์ที่ถูกสแกน
        await _dbContext.SaveChangesAsync();

        if (firstOrder.PickupLocation is null)
        {
            _logger.LogWarning("Order {OrderId} has no pickup location", firstOrder.Id);
            return;
        }

        var radiusSteps = _config.GetSection("Dispatch:SearchRadiusSteps").Get<int[]>();
        if (radiusSteps == null || radiusSteps.Length == 0)
        {
            var fallbackRadius = _config.GetValue("Dispatch:SearchRadiusKm", 5);
            radiusSteps = new[] { fallbackRadius };
        }

        var pickupLat = firstOrder.PickupLocation.Y;
        var pickupLng = firstOrder.PickupLocation.X;

        StackExchange.Redis.GeoRadiusResult[] nearbyRiders = Array.Empty<StackExchange.Redis.GeoRadiusResult>();
        Dictionary<string, Rider> ridersDict = new();
        var candidates = new List<(string RiderId, double DistanceKm, double Lat, double Lng)>();
        int usedRadiusKm = radiusSteps[^1];

        // Escalating search: ค้นหาในระยะใกล้ก่อน (เช่น 2 กม.) หากไม่พบจึงขยายเป็นระยะถัดไป (เช่น 5 กม.)
        foreach (var radius in radiusSteps)
        {
            usedRadiusKm = radius;
            nearbyRiders = await _presenceService.GetNearbyRidersAsync(pickupLat, pickupLng, radius);

            if (nearbyRiders.Length == 0)
            {
                _logger.LogInformation("No riders found within {Radius}km for order {OrderId}, checking next radius step", radius, firstOrder.Id);
                continue;
            }

            var riderIds = nearbyRiders.Select(r => r.Member.ToString()).ToList();
            ridersDict = await _dbContext.Riders
                .Where(r => riderIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id);

            candidates.Clear();
            foreach (var result in nearbyRiders)
            {
                var riderId = result.Member.ToString();

                if (!ridersDict.TryGetValue(riderId, out var rider))
                    continue;

                if (rider.State != RiderState.IDLE)
                    continue;

                if (await _lockService.IsLockedAsync(riderId))
                    continue;

                candidates.Add((riderId, result.Distance ?? 0, result.Position?.Latitude ?? 0, result.Position?.Longitude ?? 0));
            }

            // หากพบ Candidate ที่ IDLE ในระยะนี้ ให้หยุดขยายรัศมีทันที เพื่อให้ความสำคัญกับไรเดอร์ที่อยู่ใกล้สุด
            if (candidates.Count > 0)
            {
                _logger.LogInformation("Found {Count} idle riders within {Radius}km for order {OrderId}", candidates.Count, radius, firstOrder.Id);
                break;
            }

            _logger.LogInformation("Found {RidersCount} riders within {Radius}km for order {OrderId} but none were idle. Expanding search radius...", nearbyRiders.Length, radius, firstOrder.Id);
        }

        await _adminNotifier.NotifyDispatchScanStartedAsync(firstOrder, pickupLat, pickupLng, usedRadiusKm, nearbyRiders);

        if (candidates.Count == 0)
        {
            _logger.LogWarning("No idle riders available for order {OrderId} within max search radius {Radius}km", firstOrder.Id, usedRadiusKm);
            return;
        }

        // 3. ส่ง Candidates ไป Optimization service สำหรับ weighted heuristic scoring (Phase A)
        var rankedCandidates = await _ranker.RankCandidatesAsync(firstOrder, candidates, ridersDict);

        await _adminNotifier.NotifyCandidatesRankedAsync(firstOrder, rankedCandidates);

        // 4. ลองจอง Rider ทีละคนตามลำดับ
        foreach (var candidate in rankedCandidates)
        {
            var success = await TryOfferToRiderAsync(orders, candidate.RiderId);
            if (success) return; // จองได้แล้ว
        }

        _logger.LogWarning("Could not lock any rider for order {OrderId}", firstOrder.Id);
    }
}
