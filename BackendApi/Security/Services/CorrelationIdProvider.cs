using Microsoft.AspNetCore.Http;
using System;
using System.Diagnostics;
using System.Linq;

namespace BackendApi.Security.Services;

public static class CorrelationIdProvider
{
    private const string HeaderKey = "X-Correlation-Id";

    public static string GetOrCreate(IHttpContextAccessor? httpContextAccessor)
    {
        return GetOrCreate(httpContextAccessor?.HttpContext);
    }

    public static string GetOrCreate(HttpContext? httpContext)
    {
        // 1. Try HttpContext Items
        var correlationId = httpContext?.Items["CorrelationId"]?.ToString();
        if (!string.IsNullOrEmpty(correlationId))
        {
            return correlationId;
        }

        // 2. Try HTTP Request Header
        correlationId = httpContext?.Request.Headers[HeaderKey].FirstOrDefault();
        if (!string.IsNullOrEmpty(correlationId))
        {
            return correlationId;
        }

        // 3. Try W3C TraceId from current Activity (must be non-zero and 32 chars)
        var traceId = Activity.Current?.TraceId.ToString();
        if (!string.IsNullOrEmpty(traceId) && traceId != "00000000000000000000000000000000")
        {
            return traceId;
        }

        // 4. Try HttpContext TraceIdentifier
        correlationId = httpContext?.TraceIdentifier;
        if (!string.IsNullOrEmpty(correlationId))
        {
            return correlationId;
        }

        // 5. Fallback to clean 32-character GUID (W3C TraceId length match)
        return Guid.NewGuid().ToString("N");
    }
}

