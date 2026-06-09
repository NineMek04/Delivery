using Prometheus;

namespace BackendApi.Security;

public static class SecurityMetrics
{
    // 1. Failed Logins Counter
    public static readonly Counter AuthFailedLoginsTotal = Metrics.CreateCounter(
        "delivery_auth_failed_logins_total",
        "Total number of failed login attempts.",
        new CounterConfiguration { LabelNames = new[] { "reason" } }
    );

    // 2. Lockouts Counter
    public static readonly Counter AuthLockoutsTotal = Metrics.CreateCounter(
        "delivery_auth_lockouts_total",
        "Total number of account lockouts triggered."
    );

    // 3. CSRF Rejections Counter
    public static readonly Counter CsrfRejectionsTotal = Metrics.CreateCounter(
        "delivery_csrf_rejections_total",
        "Total number of CSRF verification rejections.",
        new CounterConfiguration { LabelNames = new[] { "reason" } }
    );

    // 4. Rate Limit Rejections Counter
    public static readonly Counter RateLimitRejectionsTotal = Metrics.CreateCounter(
        "delivery_rate_limit_rejections_total",
        "Total number of requests rejected by rate limits.",
        new CounterConfiguration { LabelNames = new[] { "endpoint" } }
    );

    // 5. Dispatch Queue Depth Gauge
    public static readonly Gauge DispatchQueueDepth = Metrics.CreateGauge(
        "delivery_dispatch_queue_depth",
        "Current number of jobs in the dispatch queue."
    );

    // 6. RabbitMQ Connection Status Gauge
    public static readonly Gauge RabbitMqConnectionStatus = Metrics.CreateGauge(
        "delivery_rabbitmq_connection_status",
        "Status of RabbitMQ connection (1 = Connected, 0 = Disconnected)."
    );

    // 7. RabbitMQ Reconnects Counter
    public static readonly Counter RabbitMqReconnectsTotal = Metrics.CreateCounter(
        "delivery_rabbitmq_reconnects_total",
        "Total number of RabbitMQ reconnections."
    );
}
