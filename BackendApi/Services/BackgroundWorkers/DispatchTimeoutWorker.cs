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

        // Safe initial execution on startup (prevent host crash)
        try
        {
            await CheckExpiredOffersAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial DispatchTimeoutWorker check failed on startup");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await CheckExpiredOffersAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in DispatchTimeoutWorker");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }

        _logger.LogInformation("DispatchTimeoutWorker stopped");
    }

    private async Task CheckExpiredOffersAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var offerHandler = scope.ServiceProvider.GetRequiredService<DispatchOfferHandler>();

        var now = DateTime.UtcNow;

        var expiredOrders = await dbContext.Orders
            .Where(o =>
                o.State == OrderState.OFFERING &&
                o.OfferExpiresAt != null &&
                o.OfferExpiresAt < now)
            .ToListAsync(ct);

        if (expiredOrders.Count > 0)
        {
            var uniqueOffers = expiredOrders
                .Where(o => o.CurrentOfferId != null && o.AssignedRiderId != null)
                .GroupBy(o => new { o.CurrentOfferId, o.AssignedRiderId })
                .Select(g => new { g.Key.CurrentOfferId, g.Key.AssignedRiderId, OrderIds = g.Select(o => o.Id).ToList() })
                .ToList();

            foreach (var offer in uniqueOffers)
            {
                if (ct.IsCancellationRequested) break;

                _logger.LogWarning(
                    "Offer expired: Offer {OfferId} (Orders: {OrderIds}) → Rider {RiderId}",
                    offer.CurrentOfferId, string.Join(", ", offer.OrderIds), offer.AssignedRiderId);

                await offerHandler.RejectOrTimeoutAsync(offer.CurrentOfferId!, offer.AssignedRiderId!);
            }

            _logger.LogInformation("Processed {Count} expired offers", uniqueOffers.Count);
        }
    }
}
