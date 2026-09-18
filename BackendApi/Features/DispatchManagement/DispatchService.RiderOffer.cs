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
    /// ยิง Offer ไปให้ Rider พร้อม Lock + Timer
    /// </summary>
    private async Task<bool> TryOfferToRiderAsync(List<Order> orders, string riderId, bool isInjection = false)
    {
        using var scope = BeginLogScope(orders.FirstOrDefault()?.Id, riderId);
        var offerTimeout = _config.GetValue("Dispatch:OfferTimeoutSeconds", 30);
        var offerId = $"OFF-{Guid.NewGuid():N}"[..16];
        var timeout = TimeSpan.FromSeconds(offerTimeout);

        // จอง Rider ด้วย Redis Lock — ข้ามกรณี injection เพราะ rider กำลัง BUSY อยู่แล้วและมี lock เดิม
        if (!isInjection)
        {
            if (!await _lockService.TryAcquireRiderLockAsync(riderId, offerId, timeout))
                return false;
        }

        // เปลี่ยนสถานะ Rider → RESERVED (ข้ามถ้าเป็น injection เพราะกำลัง BUSY)
        if (!isInjection)
        {
            if (!await _stateMachine.TransitionRiderAsync(riderId, RiderState.RESERVED))
            {
                await _lockService.ReleaseLockAsync(riderId, offerId);
                return false;
            }
        }

        // อัปเดต Order ด้วย Offer info
        var originalValues = orders.Select(o => new
        {
            Order = o,
            o.CurrentOfferId,
            o.OfferVersion,
            o.OfferExpiresAt,
            o.AssignedRiderId
        }).ToList();

        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            foreach (var order in orders)
            {
                order.CurrentOfferId = offerId;
                order.OfferVersion++;
                order.OfferExpiresAt = DateTime.UtcNow.Add(timeout);
                order.AssignedRiderId = riderId;

                if (!await _stateMachine.TransitionOrderAsync(order, OrderState.OFFERING))
                {
                    await transaction.RollbackAsync();

                    // Rollback properties on failure to prevent stale state in memory
                    foreach (var orig in originalValues)
                    {
                        orig.Order.CurrentOfferId = orig.CurrentOfferId;
                        orig.Order.OfferVersion = orig.OfferVersion;
                        orig.Order.OfferExpiresAt = orig.OfferExpiresAt;
                        orig.Order.AssignedRiderId = orig.AssignedRiderId;
                    }

                    if (!isInjection) await _lockService.ReleaseLockAsync(riderId, offerId);
                    if (!isInjection) await _stateMachine.TransitionRiderAsync(riderId, RiderState.IDLE);
                    return false;
                }
            }
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();

            // Rollback properties on failure to prevent stale state in memory
            foreach (var orig in originalValues)
            {
                orig.Order.CurrentOfferId = orig.CurrentOfferId;
                orig.Order.OfferVersion = orig.OfferVersion;
                orig.Order.OfferExpiresAt = orig.OfferExpiresAt;
                orig.Order.AssignedRiderId = orig.AssignedRiderId;
            }

            if (!isInjection) await _lockService.ReleaseLockAsync(riderId, offerId);
            if (!isInjection) await _stateMachine.TransitionRiderAsync(riderId, RiderState.IDLE);

            _logger.LogError(ex, "Transaction failed while offering Rider {RiderId} for offer {OfferId}", riderId, offerId);
            return false;
        }

        var firstOrder = orders.First();

        // คำนวณเส้นทาง Rider → Pickup
        string? pickupPolyline = null;
        double? pickupRouteDistanceMeters = null;
        double? pickupRouteDurationSeconds = null;
        var riderLocation = await _presenceService.GetLastKnownLocationAsync(riderId);

        if (riderLocation is not null && firstOrder.PickupLocation is not null)
        {
            try
            {
                var pickupRoute = await _routingService.GetRouteDetailsAsync(
                    riderLocation.Value.Lat,
                    riderLocation.Value.Lng,
                    firstOrder.PickupLocation.Y,
                    firstOrder.PickupLocation.X);

                pickupPolyline = pickupRoute.Polyline;
                pickupRouteDistanceMeters = pickupRoute.DistanceMeters;
                pickupRouteDurationSeconds = pickupRoute.DurationSeconds;
                OperationalMetrics.DispatchDistanceMeters.Observe(pickupRoute.DistanceMeters);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to calculate pickup route for Rider {RiderId} and Order {OrderId}. Falling back to straight line on clients.",
                    riderId,
                    firstOrder.Id);
            }
        }

        // Re-calculate ETA ด้วย OSRM pickup duration + Rider velocity จริง (สะสมลำดับขั้นตอนก่อนหน้าใน Batch)
        if (pickupRouteDurationSeconds.HasValue && firstOrder.RouteDurationSeconds > 0)
        {
            try
            {
                var riderSpeed = await _presenceService.GetRiderSpeedAsync(riderId);
                var sortedOrders = orders.OrderBy(o => o.BatchSequence).ToList();
                double cumulativePickupSeconds = pickupRouteDurationSeconds.Value;

                foreach (var order in sortedOrders)
                {
                    var etaRequest = new PredictEtaRequestDto
                    {
                        PickupLat = order.PickupLocation?.Y ?? 0,
                        PickupLng = order.PickupLocation?.X ?? 0,
                        DropoffLat = order.DropoffLocation?.Y ?? 0,
                        DropoffLng = order.DropoffLocation?.X ?? 0,
                        RouteDistanceMeters = order.RouteDistanceMeters,
                        RouteDurationSeconds = order.RouteDurationSeconds,
                        CurrentTime = DateTime.UtcNow.ToString("O"),
                        RiderSpeedKmh = riderSpeed > 0 ? riderSpeed : null,
                        OsrmPickupDurationSeconds = cumulativePickupSeconds
                    };

                    var etaResult = await _aiService.PredictEtaAsync(etaRequest);
                    if (etaResult != null && DateTime.TryParse(etaResult.EtaDatetime, out var newEta))
                    {
                        order.ExpectedDeliveryTime = newEta;
                    }

                    // สะสมระยะเวลาเดินทางและเวลาในการส่งมอบ (180 วินาที) ของออเดอร์นี้สำหรับออเดอร์ถัดไป
                    cumulativePickupSeconds += order.RouteDurationSeconds + 180;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to re-calculate ETA for Orders with Rider {RiderId}. Using original ETA.", riderId);
            }
        }

        var offerPayload = new
        {
            OfferId = offerId,
            Version = firstOrder.OfferVersion,
            ExpiresAt = firstOrder.OfferExpiresAt,
            RiderId = riderId,
            IsBatch = orders.Count > 1,
            IsInjection = isInjection,
            BatchGroupId = firstOrder.BatchGroupId,
            PickupRoute = new
            {
                EncodedPolyline = pickupPolyline,
                DistanceMeters = pickupRouteDistanceMeters,
                DurationSeconds = pickupRouteDurationSeconds,
                StartLat = riderLocation?.Lat,
                StartLng = riderLocation?.Lng,
                EndLat = firstOrder.PickupLocation?.Y,
                EndLng = firstOrder.PickupLocation?.X
            },
            Order = new
            {
                firstOrder.Id,
                PickupLat = firstOrder.PickupLocation?.Y,
                PickupLng = firstOrder.PickupLocation?.X,
                DropoffLat = firstOrder.DropoffLocation?.Y,
                DropoffLng = firstOrder.DropoffLocation?.X,
                firstOrder.SlaLimitMinutes,
                DistanceKm = firstOrder.DistanceKm,
                DeliveryFee = firstOrder.DeliveryFee,
                EncodedPolyline = firstOrder.EncodedPolyline,
                Sequence = firstOrder.BatchSequence
            },
            Orders = orders.Select(o => new
            {
                o.Id,
                PickupLat = o.PickupLocation?.Y,
                PickupLng = o.PickupLocation?.X,
                DropoffLat = o.DropoffLocation?.Y,
                DropoffLng = o.DropoffLocation?.X,
                o.SlaLimitMinutes,
                DistanceKm = o.DistanceKm,
                DeliveryFee = o.DeliveryFee,
                EncodedPolyline = o.EncodedPolyline,
                Sequence = o.BatchSequence
            }).ToList(),
            TotalDeliveryFee = orders.Sum(o => o.DeliveryFee),
            TotalDistanceKm = orders.Sum(o => o.DistanceKm)
        };

        // ส่ง Offer ไปให้ Rider ผ่าน SignalR
        await _riderNotifier.SendOfferToRiderAsync(riderId, offerPayload);

        // แจ้ง Admin Dashboard
        await _adminNotifier.NotifyOfferSentAsync(offerPayload);

        // Trigger FCM push notification to Rider in background
        _riderNotifier.SendFcmOfferNotificationInBackground(riderId, firstOrder.Id, offerId, orders.Sum(o => o.DeliveryFee), orders.Sum(o => o.DistanceKm));

        _logger.LogInformation(
            "Offer {OfferId} sent to Rider {RiderId} for {Count} Orders (Batch: {BatchId}) — expires in {Timeout}s",
            offerId, riderId, orders.Count, firstOrder.BatchGroupId, offerTimeout);

        return true;
    }

}
