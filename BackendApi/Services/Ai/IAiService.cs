using BackendApi.Models.DTOs;

namespace BackendApi.Services.Ai;

public interface IAiService
{
    Task<DispatchRankResponseDto?> RankDispatchCandidatesAsync(DispatchRankRequestDto request, CancellationToken cancellationToken = default);
    Task<RoutingResponseDto?> OptimizeRouteAsync(RoutingRequestDto request, CancellationToken cancellationToken = default);
}
