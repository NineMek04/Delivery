using BackendApi.Data;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BackendApi.Core;

[ApiController]
public abstract class DeliveryControllerBase : ControllerBase
{
    private ApplicationDbContext? _dbContext;
    private ILogger? _logger;

    protected ApplicationDbContext DbContext =>
        _dbContext ??= HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();

    protected ILogger Logger =>
        _logger ??= HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(GetType());

    protected string? CurrentUserId =>
        User?.Identity?.IsAuthenticated == true
            ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value
            : null;
}
