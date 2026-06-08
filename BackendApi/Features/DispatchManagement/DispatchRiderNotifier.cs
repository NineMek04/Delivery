using BackendApi.Data;
using BackendApi.Services.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Services.Dispatch;

/// <summary>
/// Rider Notifier — ส่ง Offer และ Push Notification ไปยัง Rider App ผ่าน SignalR + FCM
/// </summary>
public class DispatchRiderNotifier
{
    private readonly IHubContext<BackendApi.Hubs.TrackingHub> _hubContext;
    private readonly IFcmNotificationService _fcmService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<DispatchRiderNotifier> _logger;

    public DispatchRiderNotifier(
        IHubContext<BackendApi.Hubs.TrackingHub> hubContext,
        IFcmNotificationService fcmService,
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<DispatchRiderNotifier> logger)
    {
        _hubContext = hubContext;
        _fcmService = fcmService;
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// ส่ง Offer ไปให้ Rider ผ่าน SignalR
    /// </summary>
    public virtual async Task SendOfferToRiderAsync(string riderId, object offerPayload)
    {
        await _hubContext.Clients.Group($"rider:{riderId}")
            .SendAsync("OfferReceived", offerPayload);
    }

    public virtual void SendFcmOfferNotificationInBackground(string riderId, string orderId, string offerId, decimal deliveryFee, double distanceKm)
    {
        var correlationId = _httpContextAccessor.HttpContext?.Items["CorrelationId"] as string ?? Guid.NewGuid().ToString();

        _ = Task.Run(async () =>
        {
            using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var riderUser = await dbContext.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.RiderId == riderId);

                    if (riderUser != null)
                    {
                        var scopedFcmService = scope.ServiceProvider.GetRequiredService<IFcmNotificationService>();
                        await scopedFcmService.SendNotificationToUserAsync(
                            riderUser.Id,
                            "มีข้อเสนองานใหม่!",
                            $"ค่าบริการจัดส่ง: ฿{deliveryFee} | ระยะทาง: {distanceKm:F1} กม.",
                            new Dictionary<string, string>
                            {
                                { "offerId", offerId },
                                { "orderId", orderId },
                                { "type", "NEW_OFFER" }
                            });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send FCM offer notification to Rider {RiderId}", riderId);
                }
            }
        });
    }
}
