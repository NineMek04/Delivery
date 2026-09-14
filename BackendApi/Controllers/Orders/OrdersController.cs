using BackendApi.Core;
using BackendApi.Core.Constants;
using BackendApi.Core.Models;
using BackendApi.Core.Models.Response;
using BackendApi.Core.Models.Entities;
using BackendApi.Models.Entities;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using BackendApi.Security.Models;
using BackendApi.Security.Services;
using BackendApi.Services;
using BackendApi.Services.Auth;
using BackendApi.Services.Notifications;
using BackendApi.Services.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Controllers.Orders;

/// <summary>
/// API สำหรับจัดการวงจรชีวิตออเดอร์ (Multi-drop และ Dispatch)
/// </summary>
[Authorize]
public class OrdersController : DeliveryControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>
    /// สร้างออเดอร์ใหม่ และสั่งให้ระบบจัดอันดับไรเดอร์เริ่มหา Rider อัตโนมัติ (Dispatch)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = $"{AuthConstants.CustomerRole},{AuthConstants.AdminRole},{AuthConstants.DispatcherRole}")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CreateOrder(
        [FromBody] CreateOrderDto dto,
        CancellationToken cancellationToken)
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var (statusCode, response) = await _orderService.CreateOrderAsync(
            dto,
            CurrentUserId,
            role,
            cancellationToken);
        return StatusCode(statusCode, response);
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
        var (statusCode, response) = await _orderService.GetOrdersAsync(search, page, pageSize, cancellationToken);
        return StatusCode(statusCode, response);
    }

    /// <summary>
    /// ดูรายการออเดอร์ของลูกค้าที่ล็อกอินอยู่
    /// </summary>
    [HttpGet("customer")]
    [Authorize(Roles = AuthConstants.CustomerRole)]
    public async Task<ActionResult<ApiResponse<List<OrderDto>>>> GetCustomerOrders(CancellationToken cancellationToken)
    {
        var (statusCode, response) = await _orderService.GetCustomerOrdersAsync(CurrentUserId, cancellationToken);
        return StatusCode(statusCode, response);
    }

    /// <summary>
    /// เคลียร์ประวัติออร์เดอร์ของลูกค้าที่ล็อกอินอยู่
    /// </summary>
    [HttpDelete("customer/clear")]
    [Authorize(Roles = AuthConstants.CustomerRole)]
    public async Task<ActionResult<ApiResponse>> ClearCustomerOrders(CancellationToken cancellationToken)
    {
        var (statusCode, response) = await _orderService.ClearCustomerOrdersAsync(CurrentUserId, cancellationToken);
        return StatusCode(statusCode, response);
    }


    /// <summary>
    /// ดูออเดอร์เดียวตาม ID หรือ Tracking Code
    /// </summary>
    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<OrderDto>>> GetOrderById(
        string id,
        CancellationToken cancellationToken)
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var (statusCode, response) = await _orderService.GetOrderByIdAsync(
            id,
            CurrentUserId,
            role,
            cancellationToken);
        return StatusCode(statusCode, response);
    }

    /// <summary>
    /// ดูออเดอร์ที่ถูกส่งมอบให้กับ Rider ที่ล็อกอินอยู่
    /// </summary>
    [HttpGet("my")]
    [Authorize(Policy = AuthConstants.RiderPolicy)]
    public async Task<ActionResult<ApiResponse<List<OrderDto>>>> GetMyOrders(CancellationToken cancellationToken)
    {
        var (statusCode, response) = await _orderService.GetMyOrdersAsync(CurrentUserId, cancellationToken);
        return StatusCode(statusCode, response);
    }

    /// <summary>
    /// ดูออเดอร์ทั้งหมดของร้านค้าที่ StorePartner ที่ล็อกอินอยู่เป็นเจ้าของ
    /// </summary>
    [HttpGet("shop")]
    [Authorize(Roles = AuthConstants.StorePartnerRole)]
    public async Task<ActionResult<ApiResponse<List<OrderDto>>>> GetShopOrders(CancellationToken cancellationToken)
    {
        var shopIdClaim = User.FindFirst("shop_id")?.Value;
        if (string.IsNullOrWhiteSpace(shopIdClaim))
            return BadRequest(ApiResponse<List<OrderDto>>.Fail("ไม่พบ ShopId ใน Token"));

        var (statusCode, response) = await _orderService.GetShopOrdersAsync(shopIdClaim, cancellationToken);
        return StatusCode(statusCode, response);
    }

    /// <summary>
    /// อัปเดตสถานะของออเดอร์ (Rider, Admin, หรือ StorePartner)
    /// </summary>
    [HttpPatch("{id}/status")]
    [Authorize(Roles = $"{AuthConstants.RiderRole},{AuthConstants.AdminRole},{AuthConstants.DispatcherRole}")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> UpdateOrderStatus(
        string id,
        [FromBody] UpdateOrderStatusDto dto,
        CancellationToken cancellationToken)
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var (statusCode, response) = await _orderService.UpdateOrderStatusAsync(id, dto, CurrentUserId, role, cancellationToken);
        return StatusCode(statusCode, response);
    }

    /// <summary>
    /// ร้านค้าพันธมิตรยอมรับออเดอร์จากลูกค้า
    /// </summary>
    [HttpPost("{id}/accept-by-store")]
    [Authorize(Roles = AuthConstants.StorePartnerRole)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> AcceptOrderByStore(
        string id,
        CancellationToken cancellationToken = default)
    {
        var (statusCode, response) = await _orderService.AcceptOrderByStoreAsync(id, CurrentUserId, cancellationToken);
        return StatusCode(statusCode, response);
    }

    /// <summary>
    /// Store partner rejects an order that is still in CREATED state.
    /// </summary>
    [HttpPost("{id}/reject-by-store")]
    [Authorize(Roles = AuthConstants.StorePartnerRole)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> RejectOrderByStore(
        string id,
        CancellationToken cancellationToken = default)
    {
        var (statusCode, response) = await _orderService.RejectOrderByStoreAsync(
            id,
            CurrentUserId,
            cancellationToken);
        return StatusCode(statusCode, response);
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
        var (statusCode, response) = await _orderService.CancelOrderAsync(id, cancellationToken);
        return StatusCode(statusCode, response);
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
        var (statusCode, response) = await _orderService.RetryDispatchAsync(id, cancellationToken);
        return StatusCode(statusCode, response);
    }

    /// <summary>
    /// สั่งเริ่ม Batch Dispatch ออเดอร์หลายใบพร้อมกันโดยแอดมิน (Manual Batching)
    /// </summary>
    [HttpPost("batch-dispatch")]
    [Authorize(Policy = AuthConstants.OperationsPolicy)]
    public async Task<ActionResult<ApiResponse>> BatchDispatch(
        [FromBody] BatchDispatchDto dto,
        CancellationToken cancellationToken)
    {
        var (statusCode, response) = await _orderService.BatchDispatchAsync(dto, cancellationToken);
        return StatusCode(statusCode, response);
    }

    /// <summary>
    /// ส่งคะแนนความพึงพอใจและรีวิวออเดอร์หลังจัดส่งสำเร็จ
    /// </summary>
    [HttpPost("{id}/review")]
    [Authorize(Roles = $"{AuthConstants.CustomerRole},{AuthConstants.AdminRole}")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> SubmitReview(
        string id,
        [FromBody] SubmitOrderReviewDto dto,
        CancellationToken cancellationToken)
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var (statusCode, response) = await _orderService.SubmitReviewAsync(
            id,
            dto,
            CurrentUserId,
            role,
            cancellationToken);
        return StatusCode(statusCode, response);
    }

    /// <summary>
    /// ดึงประวัติเส้นทางจริงและพิกัด GPS ย้อนหลังของออเดอร์ (ตามช่วงเวลาที่ไรเดอร์วิ่งรับ-ส่งงานนี้)
    /// </summary>
    [HttpGet("{id}/route-history")]
    [Authorize(Policy = AuthConstants.OperationsPolicy)]
    public async Task<ActionResult<ApiResponse<OrderRouteHistoryDto>>> GetOrderRouteHistory(
        string id,
        CancellationToken cancellationToken = default)
    {
        var order = await DB.GetQuery<Order>(asNoTracking: true)
            .Include(o => o.Shop)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order == null)
        {
            return NotFound(ApiResponse<OrderRouteHistoryDto>.Fail("ไม่พบออเดอร์ดังกล่าว", code: "NOT_FOUND"));
        }

        string? riderName = null;
        if (!string.IsNullOrEmpty(order.AssignedRiderId))
        {
            var rider = await DB.GetQuery<Rider>(asNoTracking: true)
                .FirstOrDefaultAsync(r => r.Id == order.AssignedRiderId, cancellationToken);
            riderName = rider?.Name;
        }

        var gpsPoints = new List<RiderLocationHistoryDto>();

        // ถ้ามี AssignedRiderId ให้ดึงประวัติ GPS ของไรเดอร์ระหว่างช่วงเวลาที่รับงาน (AssignedAt) จนถึงส่งเสร็จ (CompletedAt หรือ เวลาปัจจุบัน)
        if (!string.IsNullOrEmpty(order.AssignedRiderId))
        {
            var from = order.AssignedAt ?? order.CreatedAt;
            var to = order.CompletedAt ?? DateTime.UtcNow;

            // เผื่อบัฟเฟอร์ก่อนรับงาน 5 นาที เพื่อให้เห็นจุดเริ่มต้นก่อนเข้าร้าน
            var queryFrom = from.AddMinutes(-5);
            var queryTo = to.AddMinutes(2);

            var rawPoints = await DB.GetQuery<RiderLocationHistory>(asNoTracking: true)
                .Where(h => h.RiderId == order.AssignedRiderId && h.RecordedAt >= queryFrom && h.RecordedAt <= queryTo)
                .OrderBy(h => h.RecordedAt)
                .Take(2000)
                .Select(h => new RiderLocationHistoryDto
                {
                    Lat = h.Location.Y,
                    Lng = h.Location.X,
                    RecordedAt = h.RecordedAt,
                    OrderId = h.OrderId ?? order.Id
                })
                .ToListAsync(cancellationToken);

            gpsPoints = rawPoints;
        }

        var dto = new OrderRouteHistoryDto
        {
            OrderId = order.Id,
            TrackingCode = order.TrackingCode,
            Status = order.Status,
            ShopName = order.Shop?.Name,
            DeliveryAddress = order.DeliveryAddress,
            PickupLat = order.PickupLocation?.Y,
            PickupLng = order.PickupLocation?.X,
            DropoffLat = order.DropoffLocation?.Y,
            DropoffLng = order.DropoffLocation?.X,
            AssignedRiderId = order.AssignedRiderId,
            RiderName = riderName,
            AssignedAt = order.AssignedAt,
            CompletedAt = order.CompletedAt,
            PlannedPolyline = order.EncodedPolyline,
            DistanceKm = order.DistanceKm,
            DeliveryFee = order.DeliveryFee,
            ActualGpsPoints = gpsPoints
        };

        return Ok(ApiResponse<OrderRouteHistoryDto>.Ok(dto));
    }
}


