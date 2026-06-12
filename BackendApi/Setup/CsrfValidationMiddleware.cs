using BackendApi.Core.Models;
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
                _allowedCorsOrigins = new[] { "http://localhost:4200", "http://localhost:3000", "http://localhost:5173", "http://localhost:80", "http://localhost:8080" };
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
                await context.Response.WriteAsJsonAsync(
                    ApiResponse.Fail(
                        StatusCodes.Status400BadRequest,
                        "Invalid Host Header",
                        code: "INVALID_HOST"));
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
                BackendApi.Security.SecurityMetrics.CsrfRejectionsTotal.WithLabels("missing_origin").Inc();
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    ApiResponse.Fail(
                        StatusCodes.Status403Forbidden,
                        "Missing Origin or Referer",
                        code: "CSRF_MISSING_ORIGIN"));
                return;
            }

            if (!TryGetOrigin(originToValidate, out var requestOrigin) ||
                !_allowedCorsOrigins.Any(allowedOrigin =>
                    TryGetOrigin(allowedOrigin, out var parsedAllowedOrigin) &&
                    string.Equals(requestOrigin, parsedAllowedOrigin, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("CSRF: Origin/Referer validation failed. Value: {Origin}, Path: {Path}",
                    originToValidate, context.Request.Path);
                BackendApi.Security.SecurityMetrics.CsrfRejectionsTotal.WithLabels("invalid_origin").Inc();
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    ApiResponse.Fail(
                        StatusCodes.Status403Forbidden,
                        "Origin Validation Failed",
                        code: "CSRF_INVALID_ORIGIN"));
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

                BackendApi.Security.SecurityMetrics.CsrfRejectionsTotal.WithLabels("token_mismatch").Inc();
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    ApiResponse.Fail(
                        StatusCodes.Status403Forbidden,
                        "CSRF Token Validation Failed",
                        code: "CSRF_TOKEN_MISMATCH"));
                return;
            }
        }

        await _next(context);
    }

    private static bool TryGetOrigin(string value, out string origin)
    {
        origin = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        origin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }
}
