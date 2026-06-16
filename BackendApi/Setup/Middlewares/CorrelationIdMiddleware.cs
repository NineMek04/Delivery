using Microsoft.AspNetCore.Http;
using Serilog.Context;
using System;
using System.Linq;
using System.Threading.Tasks;
using BackendApi.Security;
using BackendApi.Security.Models;
using BackendApi.Security.Services;

namespace BackendApi.Setup.Middlewares;

public class CorrelationIdMiddleware
{
    private const string HeaderKey = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = CorrelationIdProvider.GetOrCreate(context);
        
        context.Items["CorrelationId"] = correlationId;
        
        if (!context.Response.Headers.ContainsKey(HeaderKey))
        {
            context.Response.Headers.TryAdd(HeaderKey, correlationId);
        }

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}


