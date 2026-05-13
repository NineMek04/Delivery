using System.Text;
using System.Threading.RateLimiting;
using BackendApi.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
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

        if (string.IsNullOrWhiteSpace(jwtKey) ||
            jwtKey.Contains("__SET_VIA_USER_SECRETS_OR_ENV__", StringComparison.OrdinalIgnoreCase) ||
            jwtKey.Contains("replace-with", StringComparison.OrdinalIgnoreCase) ||
            jwtKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Key must be provided via user secrets or environment variables and be at least 32 characters long.");
        }

        services.AddMemoryCache();
        services.AddSingleton<LoginAttemptService>();
        services.AddScoped<ITokenService, JwtTokenService>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = configuration.GetValue("RateLimiting:Global:PermitLimit", 120),
                        Window = TimeSpan.FromMinutes(configuration.GetValue("RateLimiting:Global:WindowMinutes", 1)),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddFixedWindowLimiter(AuthRateLimitPolicy, limiterOptions =>
            {
                limiterOptions.PermitLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", 10);
                limiterOptions.Window = TimeSpan.FromMinutes(configuration.GetValue("RateLimiting:Auth:WindowMinutes", 1));
                limiterOptions.QueueLimit = 0;
                limiterOptions.AutoReplenishment = true;
            });
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
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (string.IsNullOrWhiteSpace(context.Token) &&
                        context.Request.Cookies.TryGetValue(AuthConstants.AccessTokenCookieName, out var cookieToken))
                    {
                        context.Token = cookieToken;
                    }

                    return Task.CompletedTask;
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
