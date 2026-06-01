using BackendApi.Core;
using BackendApi.Core.Constants;
using BackendApi.Core.Models;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using BackendApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Controllers.Business;

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
    /// สร้างออเดอร์ใหม่ และสั่งให้ AI เริ่มหา Rider อัตโนมัติ (Dispatch)
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CreateOrder(
        [FromBody] CreateOrderDto dto,
        CancellationToken cancellationToken)
    {
        var (statusCode, response) = await _orderService.CreateOrderAsync(dto, cancellationToken);
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
    [Authorize]
    public async Task<ActionResult<ApiResponse<List<OrderDto>>>> GetCustomerOrders(CancellationToken cancellationToken)
    {
        var (statusCode, response) = await _orderService.GetCustomerOrdersAsync(CurrentUserId, cancellationToken);
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
        var (statusCode, response) = await _orderService.GetOrderByIdAsync(id, cancellationToken);
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
    /// อัปเดตสถานะของออเดอร์ (เช่น Rider กดรับของแล้วเริ่มส่ง, ส่งเสร็จแล้ว)
    /// </summary>
    [HttpPatch("{id}/status")]
    [Authorize(Roles = $"{AuthConstants.RiderRole},{AuthConstants.AdminRole}")] // Rider หรือ Admin (Defense-in-depth)
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
    [Authorize]
    public async Task<ActionResult<ApiResponse<OrderDto>>> AcceptOrderByStore(
        string id,
        [FromQuery] string? customerId = null,
        CancellationToken cancellationToken = default)
    {
        var (statusCode, response) = await _orderService.AcceptOrderByStoreAsync(id, customerId, cancellationToken);
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
    /// ลบข้อมูลออเดอร์ทั้งหมด (สำหรับ Simulator)
    /// </summary>
    [HttpDelete("all")]
    [Authorize(Policy = AuthConstants.OperationsPolicy)]
    public async Task<ActionResult<ApiResponse>> DeleteAllOrders(CancellationToken cancellationToken)
    {
        var (statusCode, response) = await _orderService.DeleteAllOrdersAsync(cancellationToken);
        return StatusCode(statusCode, response);
    }
}
