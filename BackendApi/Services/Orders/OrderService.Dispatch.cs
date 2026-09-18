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
    public async Task<(int StatusCode, ApiResponse Response)> RetryDispatchAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var order = await _db.GetObjectByKeyAsync<Order>(id, cancellationToken);
        if (order is null)
            return (StatusCodes.Status404NotFound, ApiResponse.Fail("Order not found."));

        if (order.State != Core.StateMachines.OrderState.CREATED && order.State != Core.StateMachines.OrderState.MATCHING)
        {
            return (StatusCodes.Status400BadRequest, ApiResponse.Fail($"ไม่สามารถสั่ง Dispatch ซ้ำในสถานะ {order.State} ได้"));
        }

        var success = await _stateMachine.TransitionOrderAsync(order, Core.StateMachines.OrderState.CREATED);
        if (!success)
        {
            return (StatusCodes.Status400BadRequest, ApiResponse.Fail($"ไม่สามารถเปลี่ยนสถานะออเดอร์กลับไปเป็น CREATED ได้"));
        }

        try
        {
            var correlationId = CorrelationIdProvider.GetOrCreate(_httpContextAccessor);
            await _dispatchQueue.QueueTaskAsync(new DispatchTask(DispatchTaskType.RetryOrder, order.Id, correlationId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue dispatch task for order {OrderId}", order.Id);
        }

        return (StatusCodes.Status200OK, ApiResponse.Ok("สั่ง Dispatch ใหม่เรียบร้อย ระบบกำลังค้นหาไรเดอร์ให้ใหม่..."));
    }

    public async Task<(int StatusCode, ApiResponse Response)> BatchDispatchAsync(
        BatchDispatchDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.OrderIds == null || dto.OrderIds.Count == 0)
        {
            return (StatusCodes.Status400BadRequest, ApiResponse.Fail("กรุณาระบุรหัสออเดอร์อย่างน้อย 1 รายการ"));
        }

        var orders = await _db.GetQuery<Order>()
            .Where(o => dto.OrderIds.Contains(o.Id))
            .ToListAsync(cancellationToken);

        if (orders.Count != dto.OrderIds.Count)
        {
            return (StatusCodes.Status400BadRequest, ApiResponse.Fail("พบรหัสออเดอร์บางรายการไม่ถูกต้องในระบบ"));
        }

        // ตรวจสอบสถานะของออเดอร์
        foreach (var order in orders)
        {
            if (order.State != Core.StateMachines.OrderState.CREATED && order.State != Core.StateMachines.OrderState.MATCHING)
            {
                return (StatusCodes.Status400BadRequest, ApiResponse.Fail($"ออเดอร์ {order.RefNumber} อยู่ในสถานะ {order.State} ไม่สามารถนำมาจัดกลุ่มพ่วงได้"));
            }
        }

        var batchGroupId = Guid.NewGuid().ToString();
        var size = orders.Count;

        var orderedOrders = orders.OrderBy(o => dto.OrderIds.IndexOf(o.Id)).ToList();

        for (int i = 0; i < orderedOrders.Count; i++)
        {
            var order = orderedOrders[i];
            order.BatchGroupId = batchGroupId;
            order.BatchSequence = i + 1;
            order.BatchSize = size;
            order.State = Core.StateMachines.OrderState.CREATED;
            _db.UpdateObject(order);
        }

        await _db.CommitChangesAsync(cancellationToken);

        // Enqueue background batch dispatch task to the Channel-based queue
        try
        {
            var correlationId = CorrelationIdProvider.GetOrCreate(_httpContextAccessor);
            await _dispatchQueue.QueueTaskAsync(new DispatchTask(DispatchTaskType.BatchGroup, batchGroupId, correlationId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue batch dispatch task for Batch {BatchGroupId}", batchGroupId);
        }

        return (StatusCodes.Status200OK, ApiResponse.Ok("สร้างกลุ่มออเดอร์พ่วงเรียบร้อย ระบบกำลังค้นหาไรเดอร์เพื่อจัดส่ง..."));
    }

}
