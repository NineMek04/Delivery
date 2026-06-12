using System.Text;
using System.Threading.RateLimiting;
using System.Security.Claims;
using BackendApi.Core.Models;
using BackendApi.Data;
using BackendApi.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BackendApi.Setup;

public static class SecurityConfiguration
{
    public const string AuthRateLimitPolicy = "auth";

    public static IServiceCollection AddBackendSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtKey = configuration["Jwt:Key"];
        var issuerSigningKeys = new List<SecurityKey>();

        var keysSection = configuration.GetSection("Jwt:Keys");
        if (keysSection.Exists())
        {
            foreach (var child in keysSection.GetChildren())
            {
                if (!string.IsNullOrWhiteSpace(child.Value) && child.Value.Length >= 32)
                {
                    issuerSigningKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(child.Value)) { KeyId = child.Key });
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(jwtKey) && jwtKey.Length >= 32 && !jwtKey.Contains("__SET_VIA_USER_SECRETS_OR_ENV__", StringComparison.OrdinalIgnoreCase))
        {
            issuerSigningKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)) { KeyId = "default" });
        }

        if (issuerSigningKeys.Count == 0)
        {
            throw new InvalidOperationException("Valid Jwt:Key or Jwt:Keys must be provided via configuration and be at least 32 characters long.");
        }

        services.AddMemoryCache();
        services.AddSingleton<LoginAttemptService>();
        services.AddScoped<ITokenService, JwtTokenService>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, token) =>
            {
                SecurityMetrics.RateLimitRejectionsTotal.WithLabels("global").Inc();
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(
                    ApiResponse.Fail(
                        StatusCodes.Status429TooManyRequests,
                        "มีคำขอมากเกินไป กรุณาลองใหม่อีกครั้งในภายหลัง",
                        code: "RATE_LIMITED"),
                    token);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                                  context.Connection.RemoteIpAddress?.ToString() ??
                                  "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = configuration.GetValue("RateLimiting:Global:PermitLimit", 120),
                        Window = TimeSpan.FromMinutes(configuration.GetValue("RateLimiting:Global:WindowMinutes", 1)),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(AuthRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"auth:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", 10),
                        Window = TimeSpan.FromMinutes(configuration.GetValue("RateLimiting:Auth:WindowMinutes", 1)),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = configuration["Jwt:Issuer"],
                ValidAudience = configuration["Jwt:Audience"],
                IssuerSigningKeys = issuerSigningKeys,
                ClockSkew = TimeSpan.FromSeconds(30)
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // 1. SignalR WebSocket: อ่าน token จาก query string
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }

                    // 2. HttpOnly Cookie fallback (only if Authorization header is not present)
                    if (string.IsNullOrWhiteSpace(context.Token) &&
                        !context.Request.Headers.ContainsKey("Authorization") &&
                        context.Request.Cookies.TryGetValue(AuthConstants.AccessTokenCookieName, out var cookieToken))
                    {
                        context.Token = cookieToken;
                    }

                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    var tokenRole = context.Principal?.FindFirstValue(ClaimTypes.Role);
                    if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tokenRole))
                    {
                        context.Fail("Required identity claims are missing.");
                        return;
                    }

                    var dbContext = context.HttpContext.RequestServices
                        .GetRequiredService<ApplicationDbContext>();
                    var user = await dbContext.Users
                        .AsNoTracking()
                        .Where(candidate => candidate.Id == userId)
                        .Select(candidate => new { candidate.IsActive, candidate.Role })
                        .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

                    if (user is null || !user.IsActive ||
                        !string.Equals(user.Role, tokenRole, StringComparison.OrdinalIgnoreCase))
                    {
                        context.Fail("The account is disabled or the token is stale.");
                    }
                },
                OnChallenge = async context =>
                {
                    context.HandleResponse();

                    if (context.Response.HasStarted)
                    {
                        return;
                    }

                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(
                        ApiResponse.Fail(
                            StatusCodes.Status401Unauthorized,
                            "กรุณาเข้าสู่ระบบหรือส่ง access token ที่ถูกต้อง",
                            code: "UNAUTHORIZED"),
                        context.HttpContext.RequestAborted);
                },
                OnForbidden = async context =>
                {
                    if (context.Response.HasStarted)
                    {
                        return;
                    }

                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(
                        ApiResponse.Fail(
                            StatusCodes.Status403Forbidden,
                            "คุณไม่มีสิทธิ์เข้าถึงทรัพยากรนี้",
                            code: "FORBIDDEN"),
                        context.HttpContext.RequestAborted);
                }
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthConstants.AdminPolicy, policy =>
                policy.RequireRole(AuthConstants.AdminRole));

            options.AddPolicy(AuthConstants.OperationsPolicy, policy =>
                policy.RequireRole(AuthConstants.AdminRole, AuthConstants.DispatcherRole));

            options.AddPolicy(AuthConstants.RiderPolicy, policy =>
                policy.RequireRole(AuthConstants.AdminRole, AuthConstants.RiderRole));
        });

        return services;
    }
}
