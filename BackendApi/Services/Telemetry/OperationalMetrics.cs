using Prometheus;

namespace BackendApi.Services.Telemetry;

public static class OperationalMetrics
{
    public static readonly Gauge ActiveOrders = Metrics.CreateGauge(
        "delivery_active_orders",
        "Current number of orders that have not reached a terminal state.");

    public static readonly Gauge ActiveRiders = Metrics.CreateGauge(
        "delivery_active_riders",
        "Current number of riders that are online and not stale.");

    public static readonly Gauge DispatchMatchingOrders = Metrics.CreateGauge(
        "delivery_dispatch_matching_orders",
        "Current number of orders in MATCHING or OFFERING state.");

    public static readonly Gauge GpsUpdatesPerSecond = Metrics.CreateGauge(
        "delivery_gps_updates_per_second",
        "Current GPS update throughput observed by the telemetry aggregator.");

    public static readonly Histogram AiRequestDuration = Metrics.CreateHistogram(
        "delivery_ai_request_duration_seconds",
        "Backend-observed AI Engine request duration.",
        new HistogramConfiguration
        {
            LabelNames = ["operation"],
            Buckets = Histogram.ExponentialBuckets(0.05, 2, 10)
        });

    public static readonly Histogram OsrmRequestDuration = Metrics.CreateHistogram(
        "delivery_osrm_request_duration_seconds",
        "Backend-observed OSRM request duration.",
        new HistogramConfiguration
        {
            LabelNames = ["operation"],
            Buckets = Histogram.ExponentialBuckets(0.025, 2, 11)
        });

    public static readonly Counter TokenRefreshFailures = Metrics.CreateCounter(
        "delivery_token_refresh_failures_total",
        "Total number of rejected token refresh requests.",
        new CounterConfiguration { LabelNames = ["reason"] });
}
