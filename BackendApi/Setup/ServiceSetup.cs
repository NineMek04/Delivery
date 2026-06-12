using BackendApi.Data;
using BackendApi.Core.DataHandlers;
using BackendApi.Core.Filters;
using BackendApi.Core.Mappings;
using BackendApi.Core.Models;
using BackendApi.Infrastructure.Redis;
using BackendApi.Services.Auth;
using BackendApi.Services.BackgroundWorkers;
using BackendApi.Services.Dispatch;
using BackendApi.Services;
using BackendApi.Services.Ai;
using BackendApi.Services.Tracking;
using BackendApi.Services.Analytics;
using BackendApi.Services.Telemetry;
using BackendApi.Infrastructure.EventBus;
using BackendApi.Infrastructure.EventBus.Handlers;
using FluentValidation;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using NetTopologySuite.Geometries;
using StackExchange.Redis;
using BackendApi.Features.FleetTracking.Telemetry;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.AspNetCore.HttpOverrides;

namespace BackendApi.Setup;

public static class ServiceSetup
{
    public const string ClientCorsPolicy = "ClientDomain";

    public static IServiceCollection AddBackendApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddBackendSecurity(configuration);
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;

            // Backend ports are private/internal in production and loopback-only in dev.
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        // --- Database ---
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.UseNetTopologySuite(handleOrdinates: Ordinates.XY)));

        services.AddHealthChecks()
            .AddNpgSql(
                configuration.GetConnectionString("DefaultConnection")!,
                name: "postgresql",
                tags: ["db", "ready"])
            .AddRedis(
                configuration.GetConnectionString("Redis") ?? "localhost:6379",
                name: "redis",
                tags: ["cache", "ready"])
            .AddCheck<BackendApi.HealthChecks.PostGisHealthCheck>(
                "postgis",
                tags: ["db", "spatial", "ready"])
            .AddCheck<BackendApi.HealthChecks.DispatchQueueHealthCheck>(
                "dispatch_queue",
                tags: ["queue", "ready"])
            .AddCheck<BackendApi.HealthChecks.SignalRHealthCheck>(
                "signalr",
                tags: ["realtime", "ready"]);

        services.AddScoped<ConditionContext>();
        services.AddScoped<DBHandlerCore>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAuditLogger, AdminAuditService>();

        // --- Mapster ---
        var mapsterConfig = MappingConfig.Configure();
        services.AddSingleton(mapsterConfig);
        services.AddScoped<IMapper, ServiceMapper>();

        // --- Redis ---
        var redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        // ปิด abortConnect เพื่อไม่ให้แอปล่มถ้า Redis ยังไม่รันตอน Start
        var redisConfig = ConfigurationOptions.Parse(redisConnection);
        redisConfig.AbortOnConnectFail = false;

        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConfig));

        // --- Dispatch Services ---
        services.AddSingleton<GpsRedisRateLimiter>();
        services.AddSingleton<GpsRabbitMqPublisher>();
        services.AddScoped<RedisLockService>();
        services.AddScoped<RiderPresenceService>();
        services.AddScoped<StateMachineService>();
        services.AddScoped<IRiderPresenceManager, RiderPresenceManager>();
        services.AddScoped<DispatchCandidateRanker>();
        services.AddScoped<DispatchRiderNotifier>();
        services.AddScoped<DispatchAdminNotifier>();
        services.AddScoped<DispatchOfferHandler>();
        services.AddScoped<DispatchService>();
        services.AddScoped<BatchEvaluator>();
        services.AddScoped<OrderNotificationService>();
        services.AddScoped<ITrackingSearchService, TrackingSearchService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddSingleton<IDispatchTaskQueue, DispatchTaskQueue>();
        services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddSingleton<TelemetryAggregator>();
        services.AddScoped<TelemetryService>();
        services.AddScoped<GpsHistoryService>();
        services.AddHttpClient();
        services.AddScoped<BackendApi.Services.Notifications.IFcmNotificationService, BackendApi.Services.Notifications.FcmNotificationService>();

        // --- EventBus / RabbitMQ Message Broker ---
        services.AddSingleton<IEventBus, RabbitMqEventBus>();
        services.AddTransient<OrderCreatedIntegrationEventHandler>();
        services.AddTransient<OrderStatusChangedIntegrationEventHandler>();
        services.AddTransient<RiderLocationUpdatedIntegrationEventHandler>();
        services.AddTransient<RiderStateChangedIntegrationEventHandler>();

        // --- Background Workers (The System Janitors) ---
        services.AddHostedService<DispatchTimeoutWorker>();
        services.AddHostedService<HeartbeatMonitor>();
        services.AddHostedService<GpsRabbitMqConsumerWorker>();
        services.AddHostedService<PartitionMaintenanceWorker>();
        services.AddHostedService<TelemetryBroadcastWorker>();
        services.AddHostedService<OsrmSnapWorker>();
        services.AddHostedService<DispatchBackgroundWorker>();
        services.AddHostedService<QueuedHostedService>();
        services.AddHostedService<DbMaintenanceWorker>();

        // --- FluentValidation ---
        services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Singleton);

        // --- CORS ---
        services.AddCors(options =>
        {
            options.AddPolicy(ClientCorsPolicy, policy =>
            {
                var allowedOrigins = ResolveCorsOrigins(configuration);

                policy
                    .WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        // --- Controllers with Global Filters ---
        services.AddControllers(options =>
        {
            options.Filters.Add<GlobalResponseFilter>();
            options.Filters.Add<GlobalExceptionFilter>();
            options.Filters.Add<ValidationFilter>();
        });
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value?.Errors.Count > 0)
                    .ToDictionary(
                        entry => entry.Key,
                        entry => entry.Value!.Errors
                            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                                ? "ค่าที่ส่งมาไม่ถูกต้อง"
                                : error.ErrorMessage)
                            .ToArray());

                return new BadRequestObjectResult(ApiResponse.Fail(
                    StatusCodes.Status400BadRequest,
                    "ข้อมูลไม่ผ่านการตรวจสอบ",
                    errorDetail: string.Join(
                        "; ",
                        errors.SelectMany(entry =>
                            entry.Value.Select(message => $"{entry.Key}: {message}"))),
                    code: "VALIDATION_ERROR",
                    errors: errors));
            };
        });

        services.AddSignalR(options =>
        {
            // ป้องกัน Thundering Herd เวลาไรเดอร์เข้าพร้อมกัน 500 คน
            options.HandshakeTimeout = TimeSpan.FromSeconds(30);
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
            options.MaximumReceiveMessageSize = 32 * 1024;
            options.MaximumParallelInvocationsPerClient = 1;
        });

        // --- AI Service HttpClient ---
        services.AddHttpClient<IAiService, AiService>(client =>
        {
            var aiServiceUrl = configuration["AI_SERVICE_URL"]
                ?? configuration["Services:AiService:BaseUrl"];

            if (!string.IsNullOrWhiteSpace(aiServiceUrl))
            {
                client.BaseAddress = new Uri(aiServiceUrl);
            }

            var apiKey = configuration["AI_SERVICE_API_KEY"] ?? configuration["AiServiceApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            }
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
        .AddStandardResilienceHandler(options =>
        {
            // Total timeout across all attempts
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);

            // Timeout per attempt
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);

            // Circuit Breaker: Open circuit for 30s after 5 consecutive failures
            options.CircuitBreaker.MinimumThroughput = 5;
            options.CircuitBreaker.FailureRatio = 1.0; // Require 100% failure rate in sampling window
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

            // Set retry to 1 attempt to minimize latency (since 0 is not allowed)
            options.Retry.MaxRetryAttempts = 1;
        });

        // --- OSRM Routing HttpClient ---
        services.AddHttpClient<OsrmRoutingService>();

        // --- Swagger / OpenAPI ---
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Delivery Backend API",
                Version = "v1",
                Description = "AI-Optimized Smart Delivery Routing System — Backend API"
            });
            options.OperationFilter<StandardApiResponsesOperationFilter>();

            // Include XML Comments for Swagger descriptions
            var xmlFile = $"{typeof(Program).Assembly.GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter a valid JWT access token."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }

    private static string[] ResolveCorsOrigins(IConfiguration configuration)
    {
        var configuredOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

        if (configuredOrigins is { Length: > 0 })
        {
            return configuredOrigins
                .Where(origin => !string.IsNullOrWhiteSpace(origin))
                .Select(origin => origin.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var rawOrigins = configuration["Cors:AllowedOrigins"];

        if (string.IsNullOrWhiteSpace(rawOrigins))
        {
            return ["http://localhost:4200", "http://localhost:3000", "http://localhost:5173", "http://localhost:80", "http://localhost:8080"];
        }

        return rawOrigins
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
