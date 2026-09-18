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
    public async Task<(int StatusCode, ApiResponse<OrderDto> Response)> CreateOrderAsync(
        CreateOrderDto dto,
        string? currentUserId,
        string? role,
        CancellationToken cancellationToken)
    {
        if (role == AuthConstants.CustomerRole)
        {
            if (string.IsNullOrWhiteSpace(currentUserId))
                return (StatusCodes.Status401Unauthorized, ApiResponse<OrderDto>.Fail("User ID not found in token."));

            dto.CustomerId = currentUserId;
            if (string.IsNullOrWhiteSpace(dto.ShopId))
                return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail("ShopId is required."));
        }
        else if (role != AuthConstants.AdminRole && role != AuthConstants.DispatcherRole)
        {
            return (StatusCodes.Status403Forbidden, ApiResponse<OrderDto>.Fail("This role cannot create orders."));
        }

        if (dto.Items is { Count: > 100 })
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail("An order cannot contain more than 100 item rows."));

        var requestedDeliveryTime = dto.ExpectedDeliveryTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dto.ExpectedDeliveryTime, DateTimeKind.Utc)
            : dto.ExpectedDeliveryTime.ToUniversalTime();
        if (requestedDeliveryTime < DateTime.UtcNow.AddMinutes(-5) ||
            requestedDeliveryTime > DateTime.UtcNow.AddDays(7))
        {
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail("ExpectedDeliveryTime is outside the allowed range."));
        }

        var pickupLat = dto.PickupLat;
        var pickupLng = dto.PickupLng;

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

            if (shop.Location is null)
            {
                return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail("ร้านค้ายังไม่มีพิกัดรับสินค้า"));
            }

            pickupLat = shop.Location.Y;
            pickupLng = shop.Location.X;
        }

        // ใช้ GeometryFactory force 2D เพื่อป้องกัน "Geometry has Z dimension but column does not"
        var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
        var pickup = factory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(pickupLng, pickupLat));
        var dropoff = factory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(dto.DropoffLng, dto.DropoffLat));

        // ค้นหาเส้นทางจริงบนโครงข่ายถนนด้วย Dijkstra (OSRM)
        string encodedPolyline;
        double routeDistanceMeters;
        double routeDurationSeconds;
        double distanceKm;
        decimal deliveryFee;

        try
        {
            var route = await _routingService.GetRouteDetailsAsync(pickupLat, pickupLng, dto.DropoffLat, dto.DropoffLng);
            encodedPolyline = route.Polyline;
            routeDistanceMeters = route.DistanceMeters;
            routeDurationSeconds = route.DurationSeconds;

            distanceKm = routeDistanceMeters / 1000.0;
            deliveryFee = 30 + (decimal)(distanceKm * 10.0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate actual Dijkstra/OSRM road route for new order. Pickup: ({PickupLat}, {PickupLng}), Dropoff: ({DropoffLat}, {DropoffLng})", pickupLat, pickupLng, dto.DropoffLat, dto.DropoffLng);
            return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail("ไม่สามารถคำนวณเส้นทางจัดส่งบนถนนจริงได้ เนื่องจากระบบ Dijkstra/OSRM และโครงข่ายอินเทอร์เน็ตล้มเหลว"));
        }

        // ขอ ETA estimation จาก optimization service
        var expectedDeliveryTime = requestedDeliveryTime;
        try
        {
            var etaCurrentTime = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
            var etaRequest = new PredictEtaRequestDto
            {
                PickupLat = pickupLat,
                PickupLng = pickupLng,
                DropoffLat = dto.DropoffLat,
                DropoffLng = dto.DropoffLng,
                RouteDistanceMeters = routeDistanceMeters,
                RouteDurationSeconds = routeDurationSeconds,
                CurrentTime = etaCurrentTime.ToString("O"),
                WeatherCondition = ResolveWeatherCondition(),
                TrafficLevel = ResolveTrafficLevel(etaCurrentTime)
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
            _logger.LogWarning(ex, "Failed to get ETA estimation from optimization service, falling back to client expected time");
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
            NoteToShop = dto.NoteToShop,
            NoteToRider = dto.NoteToRider,
            DeliveryAddress = dto.DeliveryAddress,
            Items = new List<OrderItem>()
        };

        // Snapshot MenuItems details (names & prices) into OrderItems to prevent price tampering
        if (dto.Items != null && dto.Items.Any())
        {
            if (string.IsNullOrWhiteSpace(dto.ShopId))
            {
                return (StatusCodes.Status400BadRequest, ApiResponse<OrderDto>.Fail("ShopId is required when order items are provided."));
            }

            var menuItemIds = dto.Items.Select(i => i.MenuItemId).ToList();
            var menuItems = await _db.GetQuery<MenuItem>()
                .Where(m => menuItemIds.Contains(m.Id) && m.ShopId == dto.ShopId)
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

}
