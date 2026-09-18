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
        string? currentUserId,
        string? role,
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

        if (string.IsNullOrWhiteSpace(currentUserId) || string.IsNullOrWhiteSpace(role))
            return (StatusCodes.Status401Unauthorized, ApiResponse<OrderDto>.Fail("User identity is missing."));

        if (role != AuthConstants.AdminRole && role != AuthConstants.DispatcherRole)
        {
            var isAuthorized = role switch
            {
                AuthConstants.CustomerRole => order.CustomerId == currentUserId,
                AuthConstants.StorePartnerRole =>
                    order.ShopId == await _db.GetQuery<BackendApi.Models.Entities.User>(asNoTracking: true)
                        .Where(user => user.Id == currentUserId)
                        .Select(user => user.ShopId)
                        .FirstOrDefaultAsync(cancellationToken),
                AuthConstants.RiderRole =>
                    order.AssignedRiderId == await _db.GetQuery<BackendApi.Models.Entities.User>(asNoTracking: true)
                        .Where(user => user.Id == currentUserId)
                        .Select(user => user.RiderId)
                        .FirstOrDefaultAsync(cancellationToken),
                _ => false
            };

            if (!isAuthorized)
                return (StatusCodes.Status403Forbidden, ApiResponse<OrderDto>.Fail("You do not have access to this order."));
        }

        return (StatusCodes.Status200OK, ApiResponse<OrderDto>.Ok(_mapper.Map<OrderDto>(order)));
    }

    public async Task<(int StatusCode, ApiResponse<List<OrderDto>> Response)> GetMyOrdersAsync(
        string? userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userId))
            return (StatusCodes.Status401Unauthorized, ApiResponse<List<OrderDto>>.Fail("User ID not found in token."));

        var user = await _db.GetQuery<BackendApi.Models.Entities.User>(asNoTracking: true)
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

    public async Task<(int StatusCode, ApiResponse Response)> ClearCustomerOrdersAsync(
        string? customerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(customerId))
            return (StatusCodes.Status401Unauthorized, ApiResponse.Fail("User ID not found in token."));

        var orders = await _db.GetQuery<Order>()
            .Where(o => o.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            await _db.DeleteObjectAsync<Order>(order.Id, softDelete: true, cancellationToken);
        }

        await _db.CommitChangesAsync(cancellationToken);
        return (StatusCodes.Status200OK, ApiResponse.Ok("ล้างประวัติออร์เดอร์สำเร็จ"));
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

}
