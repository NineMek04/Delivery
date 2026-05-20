using BackendApi.Data;
using BackendApi.Core.DataHandlers;
using BackendApi.Core.Filters;
using BackendApi.Core.Mappings;
using BackendApi.Infrastructure.Redis;
using BackendApi.Services.Auth;
using BackendApi.Services.BackgroundWorkers;
using BackendApi.Services.Dispatch;
using BackendApi.Services;
using BackendApi.Services.Ai;
using BackendApi.Services.Tracking;
using FluentValidation;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using NetTopologySuite.Geometries;
using StackExchange.Redis;

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
        services.AddSingleton<GpsSyncBuffer>();
        services.AddScoped<RedisLockService>();
        services.AddScoped<RiderPresenceService>();
        services.AddScoped<StateMachineService>();
        services.AddScoped<DispatchService>();
        services.AddScoped<ITrackingSearchService, TrackingSearchService>();
        services.AddScoped<IOrderService, OrderService>();

        // --- Background Workers (The System Janitors) ---
        services.AddHostedService<DispatchTimeoutWorker>();
        services.AddHostedService<HeartbeatMonitor>();
        services.AddHostedService<GpsSyncWorker>();
        services.AddHostedService<PartitionMaintenanceWorker>();

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

        services.AddSignalR();

        // --- AI Service HttpClient ---
        services.AddHttpClient<IAiService, AiService>(client =>
        {
            var aiServiceUrl = configuration["AI_SERVICE_URL"]
                ?? configuration["Services:AiService:BaseUrl"];

            if (!string.IsNullOrWhiteSpace(aiServiceUrl))
            {
                client.BaseAddress = new Uri(aiServiceUrl);
            }
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
            return ["http://localhost:4200", "http://localhost:3000", "http://localhost:5173", "http://localhost:80"];
        }

        return rawOrigins
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
