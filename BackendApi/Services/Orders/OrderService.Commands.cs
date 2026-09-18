using BackendApi.Core;
using BackendApi.Core.Constants;
using BackendApi.Security;
using BackendApi.Security.Models;
using BackendApi.Security.Services;
using BackendApi.Core.DataHandlers;
using BackendApi.Core.Models;
using BackendApi.Core.Models.Response;
using BackendApi.Core.Models.Entities;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Models.DTOs;
using BackendApi.Services.Ai;
using BackendApi.Services.Dispatch;
using BackendApi.Services.Tracking;
using BackendApi.Services.Notifications;
using BackendApi.Services.BackgroundWorkers;
using BackendApi.Services.BackgroundWorkers.Queues;
using BackendApi.Services.BackgroundWorkers.Maintenance;
using BackendApi.Services.BackgroundWorkers.Jobs;
using BackendApi.Infrastructure.EventBus;
using BackendApi.Infrastructure.EventBus.Events;
using BackendApi.Infrastructure.Redis;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BackendApi.Services.Orders;

public partial class OrderService
{
    public async Task<(int StatusCode, ApiResponse<OrderDto> Response)> UpdateOrderStatusAsync(
        string id,
        UpdateOrderStatusDto dto,
        string? currentUserId,
        string? role,
        CancellationToken cancellationToken)
    {
        var order = await _db.GetObjectByKeyAsync<Order>(id, cancellationToken);
        if (order is null)
            return (StatusCodes.Status404NotFound, ApiResponse<OrderDto>.Fail("Order not found."));

        if (role != AuthConstants.AdminRole && role != AuthConstants.DispatcherRole)
        {
            if (role != AuthConstants.RiderRole)
                return (StatusCodes.Status403Forbidden, ApiResponse<OrderDto>.Fail("This role cannot update order status."));

            var user = await _db.GetObjectByKeyAsync<BackendApi.Models.Entities.User>(currentUserId ?? string.Empty, cancellationToken);
            if (user == null)
                return (StatusCodes.Status403Forbidden, ApiResponse<OrderDto>.Fail("ไม่พบข้อมูลผู้ใช้"));

            if (order.AssignedRiderId != user.RiderId)
                return (StatusCodes.Status403Forbidden, ApiResponse<OrderDto>.Fail("คุณไม่ได้รับมอบหมายให้ทำออเดอร์นี้"));
        }

        if (!Enum.TryParse<Core.StateMachines.OrderState>(dto.Status, true, out var newState))
        {
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail($"Invalid status '{dto.Status}'"));
        }

        // ตรวจสอบลำดับการจัดส่งถ้าเป็นงานพ่วง (Batch)
        if (order.BatchGroupId != null)
        {
            // ถ้าเปลี่ยนเป็น DELIVERING (ไปส่ง) หรือ COMPLETED ต้องรอจุดก่อนหน้าทำรายการเสร็จก่อน
            if (newState == Core.StateMachines.OrderState.DELIVERING || newState == Core.StateMachines.OrderState.COMPLETED)
            {
                var incompletePriorOrders = await _db.GetQuery<Order>()
                    .Where(o => o.BatchGroupId == order.BatchGroupId 
                             && o.BatchSequence < order.BatchSequence 
                             && o.State != Core.StateMachines.OrderState.COMPLETED 
                             && o.State != Core.StateMachines.OrderState.CANCELLED)
                    .AnyAsync(cancellationToken);

                if (incompletePriorOrders)
                {
                    return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail("กรุณาจัดส่งออเดอร์ลำดับก่อนหน้าให้เสร็จสิ้นก่อน"));
                }
            }
        }

        var success = await _stateMachine.TransitionOrderAsync(order, newState);
        if (!success)
        {
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail($"ไม่สามารถเปลี่ยนสถานะจาก {order.State} เป็น {newState} ได้"));
        }

        if (newState == Core.StateMachines.OrderState.CANCELLED)
        {
            await CleanupOfferReservationAfterCancellationAsync(order, cancellationToken);
        }

        if (newState == Core.StateMachines.OrderState.COMPLETED || newState == Core.StateMachines.OrderState.CANCELLED)
        {
            if (order.AssignedRiderId != null)
            {
                var hasActiveOrders = await _db.GetQuery<Order>()
                    .AnyAsync(o => o.AssignedRiderId == order.AssignedRiderId 
                                && o.Id != order.Id 
                                && (o.State == Core.StateMachines.OrderState.OFFERING
                                 || o.State == Core.StateMachines.OrderState.ASSIGNED
                                 || o.State == Core.StateMachines.OrderState.PICKING_UP 
                                 || o.State == Core.StateMachines.OrderState.DELIVERING), 
                               cancellationToken);

                if (!hasActiveOrders)
                {
                    var rider = await _db.GetObjectByKeyAsync<Rider>(order.AssignedRiderId, cancellationToken);
                    if (rider != null)
                    {
                        await _stateMachine.TransitionRiderAsync(rider, Core.StateMachines.RiderState.IDLE);
                    }
                }
            }
        }

        var resultDto = _mapper.Map<OrderDto>(order);

        return (StatusCodes.Status200OK, ApiResponse<OrderDto>.Ok(resultDto, "สถานะออเดอร์อัปเดตเรียบร้อยแล้ว"));
    }

    public async Task<(int StatusCode, ApiResponse<OrderDto> Response)> AcceptOrderByStoreAsync(
        string id,
        string? currentUserId,
        CancellationToken cancellationToken)
    {
        var order = await _db.GetObjectByKeyAsync<Order>(id, cancellationToken);
        if (order is null)
            return (StatusCodes.Status404NotFound, ApiResponse<OrderDto>.Fail("Order not found."));

        var user = await _db.GetObjectByKeyAsync<BackendApi.Models.Entities.User>(currentUserId ?? string.Empty, cancellationToken);
        if (user?.ShopId is null || order.ShopId != user.ShopId)
        {
            return (StatusCodes.Status403Forbidden, ApiResponse<OrderDto>.Fail("Store partner is not allowed to accept this order."));
        }

        if (order.State != Core.StateMachines.OrderState.CREATED)
        {
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail(
                $"ไม่สามารถยอมรับออเดอร์ในสถานะ {order.State} ได้ (ต้องอยู่ในสถานะ CREATED)"));
        }

        var success = await _stateMachine.TransitionOrderAsync(order, Core.StateMachines.OrderState.MATCHING);
        if (!success)
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail("ไม่สามารถเปลี่ยนสถานะออเดอร์ได้"));

        var resultDto = _mapper.Map<OrderDto>(order);

        try
        {
            var correlationId = _httpContextAccessor.HttpContext?.Items["CorrelationId"] as string;
            await _dispatchQueue.QueueTaskAsync(new DispatchTask(DispatchTaskType.CreateOrder, order.Id, correlationId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue dispatch task after store accepted order {OrderId}", order.Id);
        }

        if (!string.IsNullOrEmpty(order.CustomerId))
        {
            await _orderNotifier.NotifyOrderAcceptedByStoreAsync(
                order.Id, order.State.ToString(), order.CustomerId, cancellationToken);
        }

        return (StatusCodes.Status200OK, ApiResponse<OrderDto>.Ok(resultDto, "ร้านค้ายอมรับออเดอร์สำเร็จ"));
    }

    public async Task<(int StatusCode, ApiResponse<OrderDto> Response)> CancelOrderAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var order = await _db.GetObjectByKeyAsync<Order>(id, cancellationToken);
        if (order is null)
            return (StatusCodes.Status404NotFound, ApiResponse<OrderDto>.Fail("Order not found."));

        var riderId = order.AssignedRiderId;
        var success = await _stateMachine.TransitionOrderAsync(order, Core.StateMachines.OrderState.CANCELLED);
        if (!success)
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail($"ไม่สามารถยกเลิกออเดอร์ในสถานะ {order.State} ได้"));

        await CleanupOfferReservationAfterCancellationAsync(order, cancellationToken);

        if (riderId != null)
        {
            var hasActiveOrders = await _db.GetQuery<Order>()
                .AnyAsync(o => o.AssignedRiderId == riderId
                            && o.Id != order.Id 
                            && (o.State == Core.StateMachines.OrderState.OFFERING
                             || o.State == Core.StateMachines.OrderState.ASSIGNED
                             || o.State == Core.StateMachines.OrderState.PICKING_UP 
                             || o.State == Core.StateMachines.OrderState.DELIVERING), 
                           cancellationToken);

            if (!hasActiveOrders)
            {
                var rider = await _db.GetObjectByKeyAsync<Rider>(riderId, cancellationToken);
                if (rider != null)
                {
                    await _stateMachine.TransitionRiderAsync(rider, Core.StateMachines.RiderState.IDLE);
                }
            }
        }

        var resultDto = _mapper.Map<OrderDto>(order);

        return (StatusCodes.Status200OK, ApiResponse<OrderDto>.Ok(resultDto, "ยกเลิกออเดอร์สำเร็จ"));
    }

    public async Task<(int StatusCode, ApiResponse<OrderDto> Response)> RejectOrderByStoreAsync(
        string id,
        string? currentUserId,
        CancellationToken cancellationToken)
    {
        var order = await _db.GetObjectByKeyAsync<Order>(id, cancellationToken);
        if (order is null)
            return (StatusCodes.Status404NotFound, ApiResponse<OrderDto>.Fail("Order not found."));

        var user = await _db.GetObjectByKeyAsync<BackendApi.Models.Entities.User>(
            currentUserId ?? string.Empty,
            cancellationToken);
        if (user?.ShopId is null || order.ShopId != user.ShopId)
        {
            return (StatusCodes.Status403Forbidden, ApiResponse<OrderDto>.Fail(
                "Store partner is not allowed to reject this order."));
        }

        if (order.State != Core.StateMachines.OrderState.CREATED)
        {
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail(
                $"Store can reject only CREATED orders. Current state: {order.State}."));
        }

        var success = await _stateMachine.TransitionOrderAsync(
            order,
            Core.StateMachines.OrderState.CANCELLED);
        if (!success)
        {
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail(
                "Unable to reject this order."));
        }

        var resultDto = _mapper.Map<OrderDto>(order);
        return (StatusCodes.Status200OK, ApiResponse<OrderDto>.Ok(
            resultDto,
            "Store rejected the order."));
    }


    private async Task CleanupOfferReservationAfterCancellationAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        var riderId = order.AssignedRiderId;
        var offerId = order.CurrentOfferId;
        var hasSiblingOfferOrders =
            !string.IsNullOrWhiteSpace(riderId) &&
            !string.IsNullOrWhiteSpace(offerId) &&
            await _db.GetQuery<Order>()
                .AnyAsync(o => o.Id != order.Id
                            && o.AssignedRiderId == riderId
                            && o.CurrentOfferId == offerId
                            && o.State == Core.StateMachines.OrderState.OFFERING,
                    cancellationToken);

        if (!string.IsNullOrWhiteSpace(riderId) &&
            !string.IsNullOrWhiteSpace(offerId) &&
            !hasSiblingOfferOrders &&
            _lockService is not null)
        {
            await _lockService.ReleaseLockAsync(riderId, offerId);
        }

        order.CurrentOfferId = null;
        order.OfferExpiresAt = null;
        await _db.CommitChangesAsync(cancellationToken);
    }


    public async Task<(int StatusCode, ApiResponse<OrderDto> Response)> SubmitReviewAsync(
        string id,
        SubmitOrderReviewDto dto,
        string? currentUserId,
        string? role,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUserId) || string.IsNullOrWhiteSpace(role))
            return (StatusCodes.Status401Unauthorized, ApiResponse<OrderDto>.Fail("User identity is missing."));

        Order? order = null;
        var parsedRef = _searchService.ParseSearchQuery(id, TrackingPrefixes.Order);
        if (parsedRef.HasValue)
        {
            order = await _db.GetQuery<Order>()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.RefNumber == parsedRef.Value, cancellationToken);
        }
        else
        {
            order = await _db.GetQuery<Order>()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        }

        if (order is null)
            return (StatusCodes.Status404NotFound, ApiResponse<OrderDto>.Fail("ไม่พบออเดอร์ในระบบ"));

        if (role != AuthConstants.AdminRole && order.CustomerId != currentUserId)
        {
            return (StatusCodes.Status403Forbidden, ApiResponse<OrderDto>.Fail("คุณไม่มีสิทธิ์ให้คะแนนออเดอร์นี้"));
        }

        if (order.State != BackendApi.Core.StateMachines.OrderState.COMPLETED)
        {
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail("สามารถให้คะแนนได้เฉพาะออเดอร์ที่จัดส่งสำเร็จแล้วเท่านั้น"));
        }

        if (dto.Rating < 1 || dto.Rating > 5)
        {
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail("คะแนนต้องอยู่ระหว่าง 1 ถึง 5 ดาว"));
        }

        order.Rating = dto.Rating;
        order.ReviewComment = dto.ReviewComment?.Trim();
        order.ReviewedAt = DateTime.UtcNow;

        _db.UpdateObject(order);
        await _db.CommitChangesAsync(cancellationToken);

        var responseDto = _mapper.Map<OrderDto>(order);
        return (StatusCodes.Status200OK, ApiResponse<OrderDto>.Ok(responseDto, "บันทึกคะแนนและรีวิวเรียบร้อยแล้ว"));
    }
}
