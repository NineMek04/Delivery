using BackendApi.Core;
using BackendApi.Core.Models;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using BackendApi.Services.Dispatch;
using MapsterMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace BackendApi.Controllers.Business;

/// <summary>
/// API สำหรับจัดการวงจรชีวิตออเดอร์ (Multi-drop และ Dispatch)
/// </summary>
[Authorize]
public class OrdersController : DeliveryControllerBase
{
    private readonly IMapper _mapper;

    public OrdersController(IMapper mapper)
    {
        _mapper = mapper;
    }

    /// <summary>
    /// สร้างออเดอร์ใหม่ และสั่งให้ AI เริ่มหา Rider อัตโนมัติ (Dispatch)
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AuthConstants.OperationsPolicy)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CreateOrder(
        [FromBody] CreateOrderDto dto,
        CancellationToken cancellationToken)
    {
        var order = new Order
        {
            PickupLocation = new Point(dto.PickupLng, dto.PickupLat) { SRID = 4326 },
            DropoffLocation = new Point(dto.DropoffLng, dto.DropoffLat) { SRID = 4326 },
            ExpectedDeliveryTime = dto.ExpectedDeliveryTime,
            State = Core.StateMachines.OrderState.CREATED
        };

        var savedOrder = DB.InsertObject(order);
        await DB.CommitChangesAsync(cancellationToken);

        var responseDto = _mapper.Map<OrderDto>(savedOrder);

        // รัน Dispatch แบบ Background Task เพื่อไม่ให้รอ API ค้าง
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var dispatchSvc = scope.ServiceProvider.GetRequiredService<DispatchService>();
                await dispatchSvc.StartDispatchAsync(savedOrder.Id);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Background dispatch failed for order {OrderId}", savedOrder.Id);
            }
        });

        return Ok(ApiResponse<OrderDto>.Ok(responseDto, "Order created and dispatch process started."));
    }

    /// <summary>
    /// ดูรายการออเดอร์ทั้งหมด (สำหรับ Admin/Dispatcher)
    /// </summary>
    [HttpGet]
    [Authorize(Policy = AuthConstants.OperationsPolicy)]
    public async Task<ActionResult<ApiResponse<PaginatedResult<OrderDto>>>> GetOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = DB.GetQuery<Order>(asNoTracking: true);
        
        var total = await query.CountAsync(cancellationToken);
        
        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = _mapper.Map<List<OrderDto>>(orders);

        var result = new PaginatedResult<OrderDto>
        {
            Items = dtos,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };

        return Ok(ApiResponse<PaginatedResult<OrderDto>>.Ok(result));
    }

    /// <summary>
    /// ดูออเดอร์เดียวตาม ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Policy = AuthConstants.OperationsPolicy)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> GetOrderById(
        string id, 
        CancellationToken cancellationToken)
    {
        var order = await DB.GetObjectByKeyAsync<Order>(id, cancellationToken);
        if (order is null)
            return NotFound(ApiResponse<OrderDto>.Fail("Order not found."));

        return Ok(ApiResponse<OrderDto>.Ok(_mapper.Map<OrderDto>(order)));
    }

    /// <summary>
    /// ดูออเดอร์ที่ถูกส่งมอบให้กับ Rider ที่ล็อกอินอยู่
    /// </summary>
    [HttpGet("my")]
    [Authorize(Policy = AuthConstants.RiderPolicy)]
    public async Task<ActionResult<ApiResponse<List<OrderDto>>>> GetMyOrders(CancellationToken cancellationToken)
    {
        var riderId = CurrentUserId;
        if (string.IsNullOrEmpty(riderId))
            return Unauthorized(ApiResponse<List<OrderDto>>.Fail("Rider ID not found in token."));

        var orders = await DB.GetQuery<Order>(asNoTracking: true)
            .Where(o => o.AssignedRiderId == riderId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = _mapper.Map<List<OrderDto>>(orders);
        return Ok(ApiResponse<List<OrderDto>>.Ok(dtos));
    }
}
