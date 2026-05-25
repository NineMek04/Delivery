using BackendApi.Core;
using BackendApi.Core.Constants;
using BackendApi.Security;
using BackendApi.Core.DataHandlers;
using BackendApi.Core.Models;
using BackendApi.Hubs;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using BackendApi.Services.Ai;
using BackendApi.Services.Dispatch;
using BackendApi.Services.Tracking;
using BackendApi.Infrastructure.EventBus;
using BackendApi.Infrastructure.EventBus.Events;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BackendApi.Services;

public class OrderService : IOrderService
{
    private readonly DBHandlerCore _db;
    private readonly IMapper _mapper;
    private readonly StateMachineService _stateMachine;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITrackingSearchService _searchService;
    private readonly OsrmRoutingService _routingService;
    private readonly IAiService _aiService;
    private readonly IHubContext<TrackingHub> _hubContext;
    private readonly IEventBus _eventBus;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        DBHandlerCore db,
        IMapper mapper,
        StateMachineService stateMachine,
        IServiceScopeFactory scopeFactory,
        ITrackingSearchService searchService,
        OsrmRoutingService routingService,
        IAiService aiService,
        IHubContext<TrackingHub> hubContext,
        IEventBus eventBus,
        ILogger<OrderService> logger)
    {
        _db = db;
        _mapper = mapper;
        _stateMachine = stateMachine;
        _scopeFactory = scopeFactory;
        _searchService = searchService;
        _routingService = routingService;
        _aiService = aiService;
        _hubContext = hubContext;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<(int StatusCode, ApiResponse<OrderDto> Response)> CreateOrderAsync(
        CreateOrderDto dto,
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

            distanceKm = routeDistanceMeters / 1000.0;
            deliveryFee = 30 + (decimal)(distanceKm * 10.0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate actual Dijkstra/OSRM road route for new order. Pickup: ({PickupLat}, {PickupLng}), Dropoff: ({DropoffLat}, {DropoffLng})", dto.PickupLat, dto.PickupLng, dto.DropoffLat, dto.DropoffLng);
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail("ไม่สามารถคำนวณเส้นทางจัดส่งบนถนนจริงได้ เนื่องจากระบบ Dijkstra/OSRM และโครงข่ายอินเทอร์เน็ตล้มเหลว"));
        }

        // ขอ ETA Prediction จาก AI Engine
        var expectedDeliveryTime = dto.ExpectedDeliveryTime;
        try
        {
            var etaRequest = new PredictEtaRequestDto
            {
                PickupLat = dto.PickupLat,
                PickupLng = dto.PickupLng,
                DropoffLat = dto.DropoffLat,
                DropoffLng = dto.DropoffLng,
                RouteDistanceMeters = routeDistanceMeters,
                RouteDurationSeconds = routeDurationSeconds,
                CurrentTime = DateTime.UtcNow.ToString("O"),
                WeatherCondition = "clear", // Could be dynamic in future
                TrafficLevel = "normal"     // Could be dynamic in future
            };
            var etaPrediction = await _aiService.PredictEtaAsync(etaRequest, cancellationToken);
            if (etaPrediction != null && !string.IsNullOrEmpty(etaPrediction.EtaDatetime))
            {
                if (DateTime.TryParse(etaPrediction.EtaDatetime, out var aiExpectedTime))
                {
                    expectedDeliveryTime = aiExpectedTime;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get AI ETA Prediction, falling back to client expected time");
        }

        var order = new Order
        {
            PickupLocation = pickup,
            DropoffLocation = dropoff,
            DistanceKm = distanceKm,
            DeliveryFee = deliveryFee,
            ExpectedDeliveryTime = expectedDeliveryTime,
            State = Core.StateMachines.OrderState.CREATED,
            EncodedPolyline = encodedPolyline,
            RouteDistanceMeters = routeDistanceMeters,
            RouteDurationSeconds = routeDurationSeconds
        };

        var savedOrder = _db.InsertObject(order);
        await _db.CommitChangesAsync(cancellationToken);

        // Publish Order Created Integration Event asynchronously to RabbitMQ
        try
        {
            await _eventBus.PublishAsync(new OrderCreatedIntegrationEvent(
                savedOrder.Id,
                savedOrder.RefNumber,
                savedOrder.State,
                savedOrder.PickupLocation?.Y ?? 0,
                savedOrder.PickupLocation?.X ?? 0,
                savedOrder.DropoffLocation?.Y ?? 0,
                savedOrder.DropoffLocation?.X ?? 0,
                savedOrder.DistanceKm,
                savedOrder.DeliveryFee
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish OrderCreatedIntegrationEvent for Order {OrderId}", savedOrder.Id);
        }

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
                _logger.LogError(ex, "Background dispatch failed for order {OrderId}", savedOrder.Id);
            }
        });

        return (StatusCodes.Status200OK, ApiResponse<OrderDto>.Ok(responseDto, "Order created and dispatch process started."));
    }

    public async Task<(int StatusCode, ApiResponse<PaginatedResult<OrderDto>> Response)> GetOrdersAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = _db.GetQuery<Order>(asNoTracking: true);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var parsedRef = _searchService.ParseSearchQuery(search, TrackingPrefixes.Order);
            if (parsedRef.HasValue)
            {
                query = query.Where(o => o.RefNumber == parsedRef.Value);
            }
            else
            {
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

        return (StatusCodes.Status200OK, ApiResponse<PaginatedResult<OrderDto>>.Ok(result));
    }

    public async Task<(int StatusCode, ApiResponse<OrderDto> Response)> GetOrderByIdAsync(
        string id,
        CancellationToken cancellationToken)
    {
        Order? order = null;

        var parsedRef = _searchService.ParseSearchQuery(id, TrackingPrefixes.Order);
        Console.WriteLine($"[DEBUG] GetOrderById called with id: '{id}', parsedRef: {parsedRef}");

        if (parsedRef.HasValue)
        {
            order = await _db.GetQuery<Order>().FirstOrDefaultAsync(o => o.RefNumber == parsedRef.Value, cancellationToken);
            Console.WriteLine($"[DEBUG] Searched by RefNumber {parsedRef.Value}, result is null? {order == null}");
        }
        else
        {
            order = await _db.GetObjectByKeyAsync<Order>(id, cancellationToken);
            Console.WriteLine($"[DEBUG] Searched by UUID, result is null? {order == null}");
        }

        if (order is null)
            return (StatusCodes.Status404NotFound, ApiResponse<OrderDto>.Fail("Order not found."));

        return (StatusCodes.Status200OK, ApiResponse<OrderDto>.Ok(_mapper.Map<OrderDto>(order)));
    }

    public async Task<(int StatusCode, ApiResponse<List<OrderDto>> Response)> GetMyOrdersAsync(
        string? userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userId))
            return (StatusCodes.Status401Unauthorized, ApiResponse<List<OrderDto>>.Fail("User ID not found in token."));

        var user = await _db.GetQuery<BackendApi.Models.User>(asNoTracking: true)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user?.RiderId is null)
            return (StatusCodes.Status401Unauthorized, ApiResponse<List<OrderDto>>.Fail("Rider profile not linked to this user."));

        var orders = await _db.GetQuery<Order>(asNoTracking: true)
            .Where(o => o.AssignedRiderId == user.RiderId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = _mapper.Map<List<OrderDto>>(orders);
        return (StatusCodes.Status200OK, ApiResponse<List<OrderDto>>.Ok(dtos));
    }

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
            var user = await _db.GetObjectByKeyAsync<BackendApi.Models.User>(currentUserId ?? string.Empty, cancellationToken);
            if (user == null || order.AssignedRiderId != user.RiderId)
            {
                return (StatusCodes.Status403Forbidden, ApiResponse<OrderDto>.Fail("คุณไม่ได้รับมอบหมายให้ทำออเดอร์นี้"));
            }
        }

        if (!Enum.TryParse<Core.StateMachines.OrderState>(dto.Status, true, out var newState))
        {
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail($"Invalid status '{dto.Status}'"));
        }

        var success = await _stateMachine.TransitionOrderAsync(order, newState);
        if (!success)
        {
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail($"ไม่สามารถเปลี่ยนสถานะจาก {order.State} เป็น {newState} ได้"));
        }

        if (newState == Core.StateMachines.OrderState.COMPLETED || newState == Core.StateMachines.OrderState.CANCELLED)
        {
            if (order.AssignedRiderId != null)
            {
                var rider = await _db.GetObjectByKeyAsync<Rider>(order.AssignedRiderId, cancellationToken);
                if (rider != null)
                {
                    await _stateMachine.TransitionRiderAsync(rider, Core.StateMachines.RiderState.IDLE);
                }
            }
        }

        var resultDto = _mapper.Map<OrderDto>(order);

        await _hubContext.Clients.Group("admins").SendAsync(
            "OrderStatusChanged",
            order.Id,
            order.State.ToString(),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(order.AssignedRiderId))
        {
            await _hubContext.Clients.Group($"rider:{order.AssignedRiderId}").SendAsync(
                "OrderStatusChanged",
                order.Id,
                order.State.ToString(),
                cancellationToken);
        }

        return (StatusCodes.Status200OK, ApiResponse<OrderDto>.Ok(resultDto, "สถานะออเดอร์อัปเดตเรียบร้อยแล้ว"));
    }

    public async Task<(int StatusCode, ApiResponse<OrderDto> Response)> AcceptOrderByStoreAsync(
        string id,
        string? customerId,
        CancellationToken cancellationToken)
    {
        var order = await _db.GetObjectByKeyAsync<Order>(id, cancellationToken);
        if (order is null)
            return (StatusCodes.Status404NotFound, ApiResponse<OrderDto>.Fail("Order not found."));

        if (order.State != Core.StateMachines.OrderState.CREATED)
        {
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail(
                $"ไม่สามารถยอมรับออเดอร์ในสถานะ {order.State} ได้ (ต้องอยู่ในสถานะ CREATED)"));
        }

        var success = await _stateMachine.TransitionOrderAsync(order, Core.StateMachines.OrderState.MATCHING);
        if (!success)
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail("ไม่สามารถเปลี่ยนสถานะออเดอร์ได้"));

        var resultDto = _mapper.Map<OrderDto>(order);

        if (!string.IsNullOrEmpty(customerId))
        {
            await _hubContext.Clients.Group($"customer:{customerId}").SendAsync(
                "OrderAcceptedByStore",
                new { orderId = order.Id, status = order.State.ToString() },
                cancellationToken);
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

        var success = await _stateMachine.TransitionOrderAsync(order, Core.StateMachines.OrderState.CANCELLED);
        if (!success)
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail($"ไม่สามารถยกเลิกออเดอร์ในสถานะ {order.State} ได้"));

        if (order.AssignedRiderId != null)
        {
            var rider = await _db.GetObjectByKeyAsync<Rider>(order.AssignedRiderId, cancellationToken);
            if (rider != null)
            {
                await _stateMachine.TransitionRiderAsync(rider, Core.StateMachines.RiderState.IDLE);
            }
        }

        var resultDto = _mapper.Map<OrderDto>(order);
        return (StatusCodes.Status200OK, ApiResponse<OrderDto>.Ok(resultDto, "ยกเลิกออเดอร์สำเร็จ"));
    }

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

        order.State = Core.StateMachines.OrderState.CREATED;
        await _db.CommitChangesAsync(cancellationToken);

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
                _logger.LogError(ex, "Background dispatch retry failed for order {OrderId}", order.Id);
            }
        });

        return (StatusCodes.Status200OK, ApiResponse.Ok("สั่ง Dispatch ใหม่เรียบร้อย ระบบกำลังค้นหาไรเดอร์ให้ใหม่..."));
    }
}
