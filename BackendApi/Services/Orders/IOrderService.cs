using BackendApi.Core.Models;
using BackendApi.Core.Models.Response;
using BackendApi.Core.Models.Entities;
using BackendApi.Models.DTOs;

namespace BackendApi.Services.Orders;
using BackendApi.Services.Notifications;

public interface IOrderService
{
    Task<(int StatusCode, ApiResponse<OrderDto> Response)> CreateOrderAsync(
        CreateOrderDto dto,
        string? currentUserId,
        string? role,
        CancellationToken cancellationToken);
    
    Task<(int StatusCode, ApiResponse<PaginatedResult<OrderDto>> Response)> GetOrdersAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);
    
    Task<(int StatusCode, ApiResponse<OrderDto> Response)> GetOrderByIdAsync(
        string id,
        string? currentUserId,
        string? role,
        CancellationToken cancellationToken);
    
    Task<(int StatusCode, ApiResponse<List<OrderDto>> Response)> GetMyOrdersAsync(string? riderId, CancellationToken cancellationToken);

    Task<(int StatusCode, ApiResponse<List<OrderDto>> Response)> GetCustomerOrdersAsync(string? customerId, CancellationToken cancellationToken);

    Task<(int StatusCode, ApiResponse<List<OrderDto>> Response)> GetShopOrdersAsync(string shopId, CancellationToken cancellationToken);
    
    Task<(int StatusCode, ApiResponse<OrderDto> Response)> UpdateOrderStatusAsync(string id, UpdateOrderStatusDto dto, string? currentUserId, string? role, CancellationToken cancellationToken);
    
    Task<(int StatusCode, ApiResponse<OrderDto> Response)> AcceptOrderByStoreAsync(string id, string? currentUserId, CancellationToken cancellationToken);

    Task<(int StatusCode, ApiResponse<OrderDto> Response)> RejectOrderByStoreAsync(string id, string? currentUserId, CancellationToken cancellationToken);
    
    Task<(int StatusCode, ApiResponse<OrderDto> Response)> CancelOrderAsync(string id, CancellationToken cancellationToken);
    
    Task<(int StatusCode, ApiResponse Response)> RetryDispatchAsync(string id, CancellationToken cancellationToken);
    
    Task<(int StatusCode, ApiResponse Response)> BatchDispatchAsync(BatchDispatchDto dto, CancellationToken cancellationToken);
    
}



