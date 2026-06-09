using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace BackendApi.Setup;

public class CsrfValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CsrfValidationMiddleware> _logger;
    private readonly string[] _allowedCorsOrigins;
    private readonly string[] _allowedHosts;

    public CsrfValidationMiddleware(RequestDelegate next, ILogger<CsrfValidationMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        
        // Load CORS Origins
        var configuredOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (configuredOrigins is { Length: > 0 })
        {
            _allowedCorsOrigins = configuredOrigins
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => o.Trim().TrimEnd('/'))
                .ToArray();
        }
        else
        {
            var rawOrigins = configuration["Cors:AllowedOrigins"];
            if (!string.IsNullOrWhiteSpace(rawOrigins))
            {
                _allowedCorsOrigins = rawOrigins
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(o => o.TrimEnd('/'))
                    .ToArray();
            }
            else
            {
                _allowedCorsOrigins = new[] { "http://localhost:4200", "http://localhost:3000", "http://localhost:5173", "http://localhost:80" };
            }
        }

        // Load Allowed Hosts (default in ASP.NET Core appsettings)
        var allowedHostsConfig = configuration["AllowedHosts"] ?? "*";
        if (allowedHostsConfig != "*")
        {
            _allowedHosts = allowedHostsConfig.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        else
        {
            _allowedHosts = new[] { "*" };
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;

        // 1. Host Header Validation
        if (!_allowedHosts.Contains("*"))
        {
            var host = context.Request.Host.Host;
            if (!_allowedHosts.Any(h => h.Equals(host, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("CSRF: Host validation failed. Host: {Host}", host);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { Message = "Invalid Host Header" });
                return;
            }
        }

        // 2. Skip safe methods
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            await _next(context);
            return;
        }

        // 3. Skip auth endpoints
        var path = context.Request.Path.Value ?? "";
        if (path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/v1/auth/register", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Skip CSRF validation if request has Authorization header (Bearer token auth)
        if (context.Request.Headers.ContainsKey("Authorization"))
        {
            await _next(context);
            return;
        }

        // 4. Validate CSRF only if client is authenticated via Cookie (Dashboard)
        if (context.Request.Cookies.ContainsKey(BackendApi.Security.AuthConstants.AccessTokenCookieName))
        {
            // A) Origin / Referer Validation
            var origin = context.Request.Headers.Origin.FirstOrDefault();
            var referer = context.Request.Headers.Referer.FirstOrDefault();
            string? originToValidate = null;

            if (!string.IsNullOrEmpty(origin))
            {
                originToValidate = origin;
            }
            else if (!string.IsNullOrEmpty(referer))
            {
                originToValidate = referer;
            }

            if (string.IsNullOrEmpty(originToValidate))
            {
                _logger.LogWarning("CSRF: Missing Origin and Referer. IP: {Ip}, Path: {Path}",
                    context.Connection.RemoteIpAddress, context.Request.Path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { Message = "Missing Origin or Referer" });
                return;
            }

            // Verify if originToValidate is in allowed origins
            if (!_allowedCorsOrigins.Any(o => originToValidate.StartsWith(o, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("CSRF: Origin/Referer validation failed. Value: {Origin}, Path: {Path}",
                    originToValidate, context.Request.Path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { Message = "Origin Validation Failed" });
                return;
            }

            // B) Double-Submit Cookie Validation
            var cookieToken = context.Request.Cookies["XSRF-TOKEN"];
            var headerToken = context.Request.Headers["X-XSRF-TOKEN"].FirstOrDefault();

            if (string.IsNullOrEmpty(cookieToken) || string.IsNullOrEmpty(headerToken) || cookieToken != headerToken)
            {
                _logger.LogWarning("CSRF: Token validation failed. IP: {Ip}, Path: {Path}, Cookie: {Cookie}, Header: {Header}",
                    context.Connection.RemoteIpAddress, context.Request.Path,
                    string.IsNullOrEmpty(cookieToken) ? "missing" : "present",
                    string.IsNullOrEmpty(headerToken) ? "missing" : "present");

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { Message = "CSRF Token Validation Failed" });
                return;
            }
        }

        await _next(context);
    }
}
