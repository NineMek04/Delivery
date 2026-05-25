using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Models.DTOs;

namespace BackendApi.Services.Ai;

public class AiService : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiService> _logger;

    public AiService(HttpClient httpClient, ILogger<AiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<DispatchRankResponseDto?> RankDispatchCandidatesAsync(DispatchRankRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/v1/dispatch/rank", request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to rank candidates. Status: {Status}, Body: {Body}", response.StatusCode, errorBody);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<DispatchRankResponseDto>(cancellationToken: cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while calling AI Engine for dispatch ranking");
            return null;
        }
    }

    public async Task<RoutingResponseDto?> OptimizeRouteAsync(RoutingRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/optimize-route", request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to optimize route. Status: {Status}, Body: {Body}", response.StatusCode, errorBody);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<RoutingResponseDto>(cancellationToken: cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while calling AI Engine for route optimization");
            return null;
        }
    }

    public async Task<PredictEtaResponseDto?> PredictEtaAsync(PredictEtaRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/v1/predict-eta", request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to predict ETA. Status: {Status}, Body: {Body}", response.StatusCode, errorBody);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PredictEtaResponseDto>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while calling AI Engine for ETA prediction");
            return null;
        }
    }
}
