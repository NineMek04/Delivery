using BackendApi.Core;
using BackendApi.Core.Constants;
using BackendApi.Core.Models;
using BackendApi.Hubs;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using BackendApi.Services.Ai;
using BackendApi.Services.Dispatch;
using BackendApi.Services.Tracking;
using MapsterMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
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
    private readonly StateMachineService _stateMachine;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITrackingSearchService _searchService;
    private readonly OsrmRoutingService _routingService;
    private readonly IHubContext<TrackingHub> _hubContext;

    public OrdersController(
        IMapper mapper,
        StateMachineService stateMachine,
        IServiceScopeFactory scopeFactory,
        ITrackingSearchService searchService,
        OsrmRoutingService routingService)
        IHubContext<TrackingHub> hubContext)
    {
        _mapper = mapper;
        _stateMachine = stateMachine;
        _scopeFactory = scopeFactory;
        _searchService = searchService;
        _routingService = routingService;
        _hubContext = hubContext;
    }

    /// <summary>
    /// สร้างออเดอร์ใหม่ และสั่งให้ AI เริ่มหา Rider อัตโนมัติ (Dispatch)
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CreateOrder(
        [FromBody] CreateOrderDto dto,
        CancellationToken cancellationToken)
    {
        // ใช้ GeometryFactory force 2D เพื่อป้องกัน "Geometry has Z dimension but column does not"
        var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
        var pickup = factory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(dto.PickupLng, dto.PickupLat));
        var dropoff = factory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(dto.DropoffLng, dto.DropoffLat));

        // ค้นหาเส้นทางจริงบนโครงข่ายถนนด้วย Dijkstra (OSRM)
        string encodedPolyline;
        double routeDistanceMeters;
        double routeDurationSeconds;
        double distanceKm;
        decimal deliveryFee;

        try
        {
            var route = await _routingService.GetRouteDetailsAsync(dto.PickupLat, dto.PickupLng, dto.DropoffLat, dto.DropoffLng);
            encodedPolyline = route.Polyline;
            routeDistanceMeters = route.DistanceMeters;
            routeDurationSeconds = route.DurationSeconds;

            // ใช้ระยะทางจริงของโครงข่ายถนน Dijkstra แทนการประมาณการแนวเส้นตรงแบบ Haversine เพื่อความโปร่งใสสูงสุด
            distanceKm = routeDistanceMeters / 1000.0;
            deliveryFee = 30 + (decimal)(distanceKm * 10.0);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to calculate actual Dijkstra/OSRM road route for new order. Pickup: ({PickupLat}, {PickupLng}), Dropoff: ({DropoffLat}, {DropoffLng})", dto.PickupLat, dto.PickupLng, dto.DropoffLat, dto.DropoffLng);
            return BadRequest(ApiResponse<OrderDto>.Fail("ไม่สามารถคำนวณเส้นทางจัดส่งบนถนนจริงได้ เนื่องจากระบบ Dijkstra/OSRM และโครงข่ายอินเทอร์เน็ตล้มเหลว"));
        }

        var order = new Order
        {
            PickupLocation = pickup,
            DropoffLocation = dropoff,
            DistanceKm = distanceKm,
            DeliveryFee = deliveryFee,
            ExpectedDeliveryTime = dto.ExpectedDeliveryTime,
            State = Core.StateMachines.OrderState.CREATED,
            EncodedPolyline = encodedPolyline,
            RouteDistanceMeters = routeDistanceMeters,
            RouteDurationSeconds = routeDurationSeconds
        };

        var savedOrder = DB.InsertObject(order);
        await DB.CommitChangesAsync(cancellationToken);

        var responseDto = _mapper.Map<OrderDto>(savedOrder);

        // Broadcast to store partners group via SignalR
        await _hubContext.Clients.Group("stores").SendAsync(
            "OrderCreated", responseDto, cancellationToken);

        // รัน Dispatch แบบ Background Task เพื่อไม่ให้รอ API ค้าง
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
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
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = DB.GetQuery<Order>(asNoTracking: true);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var parsedRef = _searchService.ParseSearchQuery(search, TrackingPrefixes.Order);
            if (parsedRef.HasValue)
            {
                // 1. ถ้าระบุรหัสเป๊ะ ยิงตรงเข้าระบบ Index ทันที (เร็วที่สุด)
                query = query.Where(o => o.RefNumber == parsedRef.Value);
            }
            else
            {
                // 2. Fallback: ค้นหาด้วยสถานะออเดอร์ หรือ ID ของไรเดอร์ที่ได้รับมอบหมาย
                if (Enum.TryParse<BackendApi.Core.StateMachines.OrderState>(search, true, out var searchState))
                {
                    query = query.Where(o => o.State == searchState);
                }
                else
                {
                    query = query.Where(o => o.AssignedRiderId == search);
                }
            }
        }

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
    /// ดูออเดอร์เดียวตาม ID หรือ Tracking Code
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Policy = AuthConstants.OperationsPolicy)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> GetOrderById(
        string id,
        CancellationToken cancellationToken)
    {
        Order? order = null;

        var parsedRef = _searchService.ParseSearchQuery(id, TrackingPrefixes.Order);
        Console.WriteLine($"[DEBUG] GetOrderById called with id: '{id}', parsedRef: {parsedRef}");

        if (parsedRef.HasValue)
        {
            // ค้นหาด้วย Tracking Code (RefNumber Index)
            order = await DB.GetQuery<Order>().FirstOrDefaultAsync(o => o.RefNumber == parsedRef.Value, cancellationToken);
            Console.WriteLine($"[DEBUG] Searched by RefNumber {parsedRef.Value}, result is null? {order == null}");
        }
        else
        {
            // ค้นหาด้วย UUID เดิม
            order = await DB.GetObjectByKeyAsync<Order>(id, cancellationToken);
            Console.WriteLine($"[DEBUG] Searched by UUID, result is null? {order == null}");
        }

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

    /// <summary>
    /// อัปเดตสถานะของออเดอร์ (เช่น Rider กดรับของแล้วเริ่มส่ง, ส่งเสร็จแล้ว)
    /// </summary>
    [HttpPatch("{id}/status")]
    [Authorize] // Rider หรือ Admin
    public async Task<ActionResult<ApiResponse<OrderDto>>> UpdateOrderStatus(
        string id,
        [FromBody] UpdateOrderStatusDto dto,
        CancellationToken cancellationToken)
    {
        var order = await DB.GetObjectByKeyAsync<Order>(id, cancellationToken);
        if (order is null)
            return NotFound(ApiResponse<OrderDto>.Fail("Order not found."));

        // ตรวจสอบสิทธิ์ (ต้องเป็น Admin หรือเป็น Rider ที่รับงานนี้)
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role != AuthConstants.AdminRole && role != AuthConstants.DispatcherRole)
        {
            var user = await DB.GetObjectByKeyAsync<BackendApi.Models.User>(CurrentUserId ?? string.Empty, cancellationToken);
            if (user == null || order.AssignedRiderId != user.RiderId)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<OrderDto>.Fail("คุณไม่ได้รับมอบหมายให้ทำออเดอร์นี้"));
            }
        }

        if (!Enum.TryParse<Core.StateMachines.OrderState>(dto.Status, true, out var newState))
        {
            return BadRequest(ApiResponse<OrderDto>.Fail($"Invalid status '{dto.Status}'"));
        }

        // ใช้ StateMachine ป้องกันการเปลี่ยนสถานะข้ามขั้นตอน
        var success = await _stateMachine.TransitionOrderAsync(order, newState);
        if (!success)
        {
            return BadRequest(ApiResponse<OrderDto>.Fail($"ไม่สามารถเปลี่ยนสถานะจาก {order.State} เป็น {newState} ได้"));
        }

        // ถ้ายกเลิกงาน ต้องปลดล็อก Rider (แค่ถ้า Rider ยังรับงานอยู่และโดนยกเลิก)
        // หรือถ้าส่งเสร็จแล้ว (COMPLETED) ต้องเปลี่ยนสถานะ Rider ให้กลับเป็น IDLE 
        if (newState == Core.StateMachines.OrderState.COMPLETED || newState == Core.StateMachines.OrderState.CANCELLED)
        {
            if (order.AssignedRiderId != null)
            {
                var rider = await DB.GetObjectByKeyAsync<Rider>(order.AssignedRiderId, cancellationToken);
                if (rider != null)
                {
                    // เปลี่ยนให้กลับไปเป็นว่าง
                    await _stateMachine.TransitionRiderAsync(rider, Core.StateMachines.RiderState.IDLE);
                }
            }
        }

        var resultDto = _mapper.Map<OrderDto>(order);
        return Ok(ApiResponse<OrderDto>.Ok(resultDto, "สถานะออเดอร์อัปเดตเรียบร้อยแล้ว"));
    }

    /// <summary>
    /// ร้านค้าพันธมิตรยอมรับออเดอร์จากลูกค้า
    /// </summary>
    [HttpPost("{id}/accept-by-store")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<OrderDto>>> AcceptOrderByStore(
        string id,
        [FromQuery] string? customerId = null,
        CancellationToken cancellationToken = default)
    {
        var order = await DB.GetObjectByKeyAsync<Order>(id, cancellationToken);
        if (order is null)
            return NotFound(ApiResponse<OrderDto>.Fail("Order not found."));

        if (order.State != Core.StateMachines.OrderState.CREATED)
        {
            return BadRequest(ApiResponse<OrderDto>.Fail(
                $"ไม่สามารถยอมรับออเดอร์ในสถานะ {order.State} ได้ (ต้องอยู่ในสถานะ CREATED)"));
        }

        // Update to MATCHING state (store accepted, now looking for rider)
        var success = await _stateMachine.TransitionOrderAsync(order, Core.StateMachines.OrderState.MATCHING);
        if (!success)
            return BadRequest(ApiResponse<OrderDto>.Fail("ไม่สามารถเปลี่ยนสถานะออเดอร์ได้"));

        var resultDto = _mapper.Map<OrderDto>(order);

        // Notify the customer via SignalR
        if (!string.IsNullOrEmpty(customerId))
        {
            await _hubContext.Clients.Group($"customer:{customerId}").SendAsync(
                "OrderAcceptedByStore",
                new { orderId = order.Id, status = order.State.ToString() },
                cancellationToken);
        }

        return Ok(ApiResponse<OrderDto>.Ok(resultDto, "ร้านค้ายอมรับออเดอร์สำเร็จ"));
    }

    /// <summary>
    /// ยกเลิกออเดอร์ (Admin/Dispatcher)
    /// </summary>
    [HttpPost("{id}/cancel")]
    [Authorize(Policy = AuthConstants.OperationsPolicy)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CancelOrder(
        string id,
        CancellationToken cancellationToken)
    {
        var order = await DB.GetObjectByKeyAsync<Order>(id, cancellationToken);
        if (order is null)
            return NotFound(ApiResponse<OrderDto>.Fail("Order not found."));

        var success = await _stateMachine.TransitionOrderAsync(order, Core.StateMachines.OrderState.CANCELLED);
        if (!success)
            return BadRequest(ApiResponse<OrderDto>.Fail($"ไม่สามารถยกเลิกออเดอร์ในสถานะ {order.State} ได้"));

        if (order.AssignedRiderId != null)
        {
            var rider = await DB.GetObjectByKeyAsync<Rider>(order.AssignedRiderId, cancellationToken);
            if (rider != null)
            {
                await _stateMachine.TransitionRiderAsync(rider, Core.StateMachines.RiderState.IDLE);
            }
        }

        var resultDto = _mapper.Map<OrderDto>(order);
        return Ok(ApiResponse<OrderDto>.Ok(resultDto, "ยกเลิกออเดอร์สำเร็จ"));
    }

    /// <summary>
    /// สั่งเริ่ม Dispatch ออเดอร์อีกครั้ง (กรณีค้าง หรือไม่มีคนขับตอนแรกรอบแรก)
    /// </summary>
    [HttpPost("{id}/dispatch")]
    [Authorize(Policy = AuthConstants.OperationsPolicy)]
    public async Task<ActionResult<ApiResponse>> RetryDispatch(
        string id,
        CancellationToken cancellationToken)
    {
        var order = await DB.GetObjectByKeyAsync<Order>(id, cancellationToken);
        if (order is null)
            return NotFound(ApiResponse.Fail("Order not found."));

        if (order.State != Core.StateMachines.OrderState.CREATED && order.State != Core.StateMachines.OrderState.MATCHING)
        {
            return BadRequest(ApiResponse.Fail($"ไม่สามารถสั่ง Dispatch ซ้ำในสถานะ {order.State} ได้"));
        }

        // ถ้ายกเลิกไปแล้ว หรือเสร็จไปแล้ว เราจะไม่ Dispatch

        // ให้เปลี่ยนสถานะกลับไป CREATED ก่อน เพื่อให้ StartDispatchAsync ทำงานได้
        order.State = Core.StateMachines.OrderState.CREATED;
        await DB.CommitChangesAsync(cancellationToken);

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatchSvc = scope.ServiceProvider.GetRequiredService<DispatchService>();
                await dispatchSvc.StartDispatchAsync(order.Id);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Background dispatch retry failed for order {OrderId}", order.Id);
            }
        });

        return Ok(ApiResponse.Ok("สั่ง Dispatch ใหม่เรียบร้อย ระบบกำลังค้นหาไรเดอร์ให้ใหม่..."));
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var r = 6371e3; // Earth's radius in meters
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        var deltaPhi = (lat2 - lat1) * Math.PI / 180;
        var deltaLambda = (lon2 - lon1) * Math.PI / 180;

        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return r * c;
    }

}
