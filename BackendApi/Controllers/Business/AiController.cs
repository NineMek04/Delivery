using BackendApi.Core;
using BackendApi.Core.Models;
using BackendApi.Models.DTOs;
using BackendApi.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Controllers.Business;

/// <summary>
/// API สำหรับการประมวลผลและการใช้บริการ AI Engine (VRP / ETA / Scoring)
/// </summary>
[Authorize]
public class AiController : DeliveryControllerBase
{
    private readonly IAiService _aiService;

    public AiController(IAiService aiService)
    {
        _aiService = aiService;
    }

    /// <summary>
    /// ส่งพิกัดทั้งหมดเพื่อหาลำดับจุดส่งที่สั้นที่สุดผ่าน VRP Solver
    /// </summary>
    [HttpPost("optimize-route")]
    public async Task<ActionResult<ApiResponse<RoutingResponseDto>>> OptimizeRoute(
        [FromBody] RoutingRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _aiService.OptimizeRouteAsync(request, cancellationToken);
        if (result == null)
        {
            return BadRequest(ApiResponse<RoutingResponseDto>.Fail("ไม่สามารถคำนวณเส้นทาง VRP ได้"));
        }

        return Ok(ApiResponse<RoutingResponseDto>.Ok(result, "คำนวณเส้นทาง VRP สำเร็จ"));
    }
}
