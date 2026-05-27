using System.Threading.Tasks;
using BackendApi.Core.StateMachines;

namespace BackendApi.Services.Tracking
{
    public record RiderConnectionResult(string RiderId, RiderState State, RiderState? PreviousState = null);
    public record RiderStatusUpdateResult(bool Success, string? Message = null, RiderState? State = null, RiderState? PreviousState = null);

    public interface IRiderPresenceManager
    {
        Task<RiderConnectionResult?> HandleRiderConnectAsync(string userId);
        Task<RiderConnectionResult?> HandleRiderConnectionDisconnectAsync(string userId);
        Task<RiderState?> HandleRiderHeartbeatAsync(string riderId);
        Task<RiderStatusUpdateResult> HandleRiderStatusUpdateAsync(string riderId, string status);
        Task<string?> GetRiderIdByUserIdAsync(string userId);
        Task<bool> HasActiveJobAsync(string riderId);
    }
}
