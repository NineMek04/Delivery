using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace BackendApi.Hubs.Tracking;

public partial class TrackingHub
{
    public async Task AcceptOffer(string offerId, int version)
    {
        var riderId = await GetRiderIdAsync();
        if (riderId is null) return;

        var success = await _offerHandler.AcceptOfferAsync(riderId, offerId, version);

        if (success)
        {
            await Clients.Caller.SendAsync("OfferAcceptedResult", new { Success = true });
        }
        else
        {
            await Clients.Caller.SendAsync("OfferAcceptedResult", new { Success = false, Message = "งานนี้หลุดไปแล้ว หรือมีผู้รับแล้ว" });
        }
    }

    public async Task RejectOffer(string offerId, string orderId)
    {
        var riderId = await GetRiderIdAsync();
        if (riderId is null) return;

        await _offerHandler.RejectOrTimeoutAsync(offerId, riderId);
    }
}

