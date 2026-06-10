using BackendApi.Core;
using BackendApi.Core.Constants;
using BackendApi.Security;
using BackendApi.Core.DataHandlers;
using BackendApi.Core.Models;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using BackendApi.Services.Ai;
using BackendApi.Services.Dispatch;
using BackendApi.Services.Tracking;
using BackendApi.Services.BackgroundWorkers;
using BackendApi.Infrastructure.EventBus;
using BackendApi.Infrastructure.EventBus.Events;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
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
    private readonly OrderNotificationService _orderNotifier;
    private readonly IEventBus _eventBus;
    private readonly IDispatchTaskQueue _dispatchQueue;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        DBHandlerCore db,
        IMapper mapper,
        StateMachineService stateMachine,
        IServiceScopeFactory scopeFactory,
        ITrackingSearchService searchService,
        OsrmRoutingService routingService,
        IAiService aiService,
        OrderNotificationService orderNotifier,
        IEventBus eventBus,
        IDispatchTaskQueue dispatchQueue,
        IHttpContextAccessor httpContextAccessor,
        ILogger<OrderService> logger)
    {
        _db = db;
        _mapper = mapper;
        _stateMachine = stateMachine;
        _scopeFactory = scopeFactory;
        _searchService = searchService;
        _routingService = routingService;
        _aiService = aiService;
        _orderNotifier = orderNotifier;
        _eventBus = eventBus;
        _dispatchQueue = dispatchQueue;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<(int StatusCode, ApiResponse<OrderDto> Response)> CreateOrderAsync(
        CreateOrderDto dto,
        CancellationToken cancellationToken)
    {
        // ตรวจสอบสถานะการเปิดร้านของร้านค้าก่อนการสั่งซื้อ
        if (!string.IsNullOrWhiteSpace(dto.ShopId))
        {
            var shop = await _db.GetQuery<Shop>()
                .FirstOrDefaultAsync(s => s.Id == dto.ShopId, cancellationToken);
            if (shop == null)
            {
                return (StatusCodes.Status404NotFound, ApiResponse<OrderDto>.Fail("ไม่พบร้านค้าที่ต้องการสั่งซื้อ"));
            }
            if (!shop.IsOpen)
            {
                return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail("ร้านค้านี้ปิดทำการชั่วคราว ไม่สามารถสั่งซื้ออาหารได้"));
            }
        }

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
        var expectedDeliveryTime = dto.ExpectedDeliveryTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dto.ExpectedDeliveryTime, DateTimeKind.Utc)
            : dto.ExpectedDeliveryTime.ToUniversalTime();
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
                    expectedDeliveryTime = aiExpectedTime.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(aiExpectedTime, DateTimeKind.Utc)
                        : aiExpectedTime.ToUniversalTime();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get AI ETA Prediction, falling back to client expected time");
        }

        var order = new Order
        {
            CustomerId = string.IsNullOrWhiteSpace(dto.CustomerId) ? null : dto.CustomerId,
            ShopId = string.IsNullOrWhiteSpace(dto.ShopId) ? null : dto.ShopId,
            PickupLocation = pickup,
            DropoffLocation = dropoff,
            DistanceKm = distanceKm,
            DeliveryFee = deliveryFee,
            ExpectedDeliveryTime = expectedDeliveryTime,
            State = Core.StateMachines.OrderState.CREATED,
            EncodedPolyline = encodedPolyline,
            RouteDistanceMeters = routeDistanceMeters,
            RouteDurationSeconds = routeDurationSeconds,
            Items = new List<OrderItem>()
        };

        // Snapshot MenuItems details (names & prices) into OrderItems to prevent price tampering
        if (dto.Items != null && dto.Items.Any())
        {
            var menuItemIds = dto.Items.Select(i => i.MenuItemId).ToList();
            var menuItems = await _db.GetQuery<MenuItem>()
                .Where(m => menuItemIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, cancellationToken);

            foreach (var itemDto in dto.Items)
            {
                if (menuItems.TryGetValue(itemDto.MenuItemId, out var menuItem))
                {
                    order.Items.Add(new OrderItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        MenuItemId = itemDto.MenuItemId,
                        Name = menuItem.Name,
                        UnitPrice = menuItem.Price,
                        Quantity = itemDto.Quantity,
                        Notes = itemDto.Notes,
                        OptionsDescription = itemDto.OptionsDescription
                    });
                }
                else
                {
                    return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail($"ไม่พบรหัสสินค้าเมนู: {itemDto.MenuItemId} ในระบบ"));
                }
            }
        }

        var savedOrder = _db.InsertObject(order);
        await _db.CommitChangesAsync(cancellationToken);

        // Publish Order Created Integration Event asynchronously to RabbitMQ
        try
        {
            var correlationId = CorrelationIdProvider.GetOrCreate(_httpContextAccessor);

            await _eventBus.PublishAsync(new OrderCreatedIntegrationEvent(
                savedOrder.Id,
                savedOrder.RefNumber,
                savedOrder.State,
                savedOrder.PickupLocation?.Y ?? 0,
                savedOrder.PickupLocation?.X ?? 0,
                savedOrder.DropoffLocation?.Y ?? 0,
                savedOrder.DropoffLocation?.X ?? 0,
                savedOrder.DistanceKm,
                savedOrder.DeliveryFee,
                correlationId
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish OrderCreatedIntegrationEvent for Order {OrderId}", savedOrder.Id);
        }

        var responseDto = _mapper.Map<OrderDto>(savedOrder);

        // Broadcast to the specific store's group via SignalR
        await _orderNotifier.NotifyOrderCreatedAsync(responseDto, cancellationToken, shopId: savedOrder.ShopId);

        return (StatusCodes.Status200OK, ApiResponse<OrderDto>.Ok(responseDto, "Order created successfully. Waiting for store acceptance."));
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
        _logger.LogDebug("GetOrderById called with id: '{Id}', parsedRef: {ParsedRef}", id, parsedRef);

        if (parsedRef.HasValue)
        {
            order = await _db.GetQuery<Order>()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.RefNumber == parsedRef.Value, cancellationToken);
            _logger.LogDebug("Searched by RefNumber {RefNumber}, result is null? {IsNull}", parsedRef.Value, order == null);
        }
        else
        {
            order = await _db.GetQuery<Order>()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
            _logger.LogDebug("Searched by UUID '{Id}', result is null? {IsNull}", id, order == null);
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
            return (StatusCodes.Status403Forbidden, ApiResponse<List<OrderDto>>.Fail("Rider profile not linked to this user."));

        var orders = await _db.GetQuery<Order>(asNoTracking: true)
            .Include(o => o.Items)
            .Where(o => o.AssignedRiderId == user.RiderId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = _mapper.Map<List<OrderDto>>(orders);
        return (StatusCodes.Status200OK, ApiResponse<List<OrderDto>>.Ok(dtos));
    }

    public async Task<(int StatusCode, ApiResponse<List<OrderDto>> Response)> GetCustomerOrdersAsync(
        string? customerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(customerId))
            return (StatusCodes.Status401Unauthorized, ApiResponse<List<OrderDto>>.Fail("User ID not found in token."));

        var orders = await _db.GetQuery<Order>(asNoTracking: true)
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = _mapper.Map<List<OrderDto>>(orders);
        return (StatusCodes.Status200OK, ApiResponse<List<OrderDto>>.Ok(dtos));
    }

    public async Task<(int StatusCode, ApiResponse<List<OrderDto>> Response)> GetShopOrdersAsync(
        string shopId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shopId))
            return (StatusCodes.Status400BadRequest, ApiResponse<List<OrderDto>>.Fail("ShopId is required."));

        var orders = await _db.GetQuery<Order>(asNoTracking: true)
            .Include(o => o.Items)
            .Where(o => o.ShopId == shopId)
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
            if (user == null)
                return (StatusCodes.Status403Forbidden, ApiResponse<OrderDto>.Fail("ไม่พบข้อมูลผู้ใช้"));

            if (role == AuthConstants.StorePartnerRole)
            {
                // StorePartner สามารถ update status ได้เฉพาะ order ของร้านตัวเอง
                if (user.ShopId == null || order.ShopId != user.ShopId)
                    return (StatusCodes.Status403Forbidden, ApiResponse<OrderDto>.Fail("คุณไม่ได้เป็นเจ้าของร้านที่รับออเดอร์นี้"));
            }
            else
            {
                // Rider: ต้องเป็น rider ที่ถูก assign
                if (order.AssignedRiderId != user.RiderId)
                    return (StatusCodes.Status403Forbidden, ApiResponse<OrderDto>.Fail("คุณไม่ได้รับมอบหมายให้ทำออเดอร์นี้"));
            }
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

        if (newState == Core.StateMachines.OrderState.COMPLETED || newState == Core.StateMachines.OrderState.CANCELLED)
        {
            if (order.AssignedRiderId != null)
            {
                var hasActiveOrders = await _db.GetQuery<Order>()
                    .AnyAsync(o => o.AssignedRiderId == order.AssignedRiderId 
                                && o.Id != order.Id 
                                && (o.State == Core.StateMachines.OrderState.ASSIGNED 
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

        await _orderNotifier.NotifyOrderStatusChangedAsync(order, cancellationToken);

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

        var user = await _db.GetObjectByKeyAsync<BackendApi.Models.User>(currentUserId ?? string.Empty, cancellationToken);
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

        await _orderNotifier.NotifyOrderStatusChangedAsync(order, cancellationToken);

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

        var success = await _stateMachine.TransitionOrderAsync(order, Core.StateMachines.OrderState.CANCELLED);
        if (!success)
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail($"ไม่สามารถยกเลิกออเดอร์ในสถานะ {order.State} ได้"));

        if (order.AssignedRiderId != null)
        {
            var hasActiveOrders = await _db.GetQuery<Order>()
                .AnyAsync(o => o.AssignedRiderId == order.AssignedRiderId 
                            && o.Id != order.Id 
                            && (o.State == Core.StateMachines.OrderState.ASSIGNED 
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

        var resultDto = _mapper.Map<OrderDto>(order);

        await _orderNotifier.NotifyOrderStatusChangedAsync(order, cancellationToken);

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

    public async Task<(int StatusCode, ApiResponse Response)> DeleteAllOrdersAsync(CancellationToken cancellationToken)
    {
        // DISABLED FOR SECURITY: DeleteAllOrdersAsync is highly dangerous in production.
        // Uncomment the code below only if you are in a dev/test environment and know what you are doing.
        /*
        await _db.GetQuery<Order>().ExecuteDeleteAsync(cancellationToken);
        return (StatusCodes.Status200OK, ApiResponse.Ok("ลบข้อมูลออเดอร์ทั้งหมดสำเร็จ"));
        */
        return await Task.FromResult((StatusCodes.Status403Forbidden, ApiResponse.Fail("การเข้าถึงฟังก์ชันนี้ถูกปฏิเสธเนื่องจากความปลอดภัย (Disabled in production)")));
    }
}
