using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Services.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Services.BackgroundWorkers;

/// <summary>
/// Dispatch Timeout Worker — สแกนงานสถานะ OFFERING ที่หมดเวลาแล้ว
/// ไม่พึ่ง Redis TTL expiration callback อย่างเดียว (ตาม feedback)
/// ใช้ event-based + periodic scan เป็น double-check
/// </summary>
public class DispatchTimeoutWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DispatchTimeoutWorker> _logger;

    public DispatchTimeoutWorker(IServiceProvider serviceProvider, ILogger<DispatchTimeoutWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DispatchTimeoutWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckExpiredOffersAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DispatchTimeoutWorker");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("DispatchTimeoutWorker stopped");
    }

    private async Task CheckExpiredOffersAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var offerHandler = scope.ServiceProvider.GetRequiredService<DispatchOfferHandler>();

        var now = DateTime.UtcNow;

        // หา Orders ที่ OFFERING แล้วเกิน OfferExpiresAt
        var expiredOrders = await dbContext.Orders
            .Where(o =>
                o.State == OrderState.OFFERING &&
                o.OfferExpiresAt != null &&
                o.OfferExpiresAt < now)
            .ToListAsync(ct);

        foreach (var order in expiredOrders)
        {
            if (ct.IsCancellationRequested) break;
            if (order.AssignedRiderId is null || order.CurrentOfferId is null)
                continue;

            _logger.LogWarning(
                "Offer expired: Order {OrderId} → Rider {RiderId} (offer {OfferId})",
                order.Id, order.AssignedRiderId, order.CurrentOfferId);

            await offerHandler.RejectOrTimeoutAsync(
                order.CurrentOfferId, order.AssignedRiderId);
        }

        if (expiredOrders.Count > 0)
        {
            _logger.LogInformation("Processed {Count} expired offers", expiredOrders.Count);
        }
    }
}
