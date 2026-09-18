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

public partial class OrderService : IOrderService
{
    private readonly DBHandlerCore _db;
    private readonly IMapper _mapper;
    private readonly StateMachineService _stateMachine;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITrackingSearchService _searchService;
    private readonly OsrmRoutingService _routingService;
    private readonly IAiService _aiService;
    private readonly OrderNotificationService _orderNotifier;
    private readonly IEventBus _eventBus;
    private readonly IDispatchTaskQueue _dispatchQueue;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<OrderService> _logger;
    private readonly RedisLockService? _lockService;
    private readonly IConfiguration? _configuration;

    public OrderService(
        DBHandlerCore db,
        IMapper mapper,
        StateMachineService stateMachine,
        IServiceScopeFactory scopeFactory,
        ITrackingSearchService searchService,
        OsrmRoutingService routingService,
        IAiService aiService,
        OrderNotificationService orderNotifier,
        IEventBus eventBus,
        IDispatchTaskQueue dispatchQueue,
        IHttpContextAccessor httpContextAccessor,
        ILogger<OrderService> logger,
        RedisLockService? lockService = null,
        IConfiguration? configuration = null)
    {
        _db = db;
        _mapper = mapper;
        _stateMachine = stateMachine;
        _scopeFactory = scopeFactory;
        _searchService = searchService;
        _routingService = routingService;
        _aiService = aiService;
        _orderNotifier = orderNotifier;
        _eventBus = eventBus;
        _dispatchQueue = dispatchQueue;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _lockService = lockService;
        _configuration = configuration;
    }


    private string ResolveWeatherCondition()
    {
        return _configuration?["EtaPrediction:WeatherCondition"]?
            .Trim()
            .ToLowerInvariant() ?? "clear";
    }

    private string ResolveTrafficLevel(DateTimeOffset localTime)
    {
        var configured = _configuration?["EtaPrediction:TrafficLevel"]?
            .Trim()
            .ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return localTime.Hour switch
        {
            >= 7 and <= 9 => "heavy",
            >= 17 and <= 19 => "heavy",
            >= 22 or <= 5 => "light",
            _ => "normal"
        };
    }

}
