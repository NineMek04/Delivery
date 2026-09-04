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

    public static readonly Histogram RouteOptimizerRequestDuration = Metrics.CreateHistogram(
        "delivery_route_optimizer_request_duration_seconds",
        "Backend-observed route optimizer service request duration.",
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

    public static readonly Gauge IdleRiders = Metrics.CreateGauge(
        "delivery_idle_riders",
        "Current number of online riders in IDLE or RESERVED state.");

    public static readonly Gauge BusyRiders = Metrics.CreateGauge(
        "delivery_busy_riders",
        "Current number of online riders in BUSY state.");

    public static readonly Gauge DispatchBacklogOrders = Metrics.CreateGauge(
        "delivery_dispatch_backlog_orders",
        "Number of orders in MATCHING or OFFERING state for more than 2 minutes.");

    public static readonly Counter RoutingRequestsTotal = Metrics.CreateCounter(
        "delivery_routing_requests_total",
        "Total number of routing requests by type.",
        new CounterConfiguration { LabelNames = new[] { "type" } });

    public static readonly Histogram DispatchDistanceMeters = Metrics.CreateHistogram(
        "delivery_dispatch_distance_meters",
        "Distance (meters) the rider must travel to pick up the order.",
        new HistogramConfiguration
        {
            Buckets = new double[] { 100, 300, 500, 800, 1200, 1600, 2000, 3000, 5000, 10000 }
        });

    public static readonly Counter DispatchMatchesTotal = Metrics.CreateCounter(
        "delivery_dispatch_matches_total",
        "Total number of dispatch match outcomes by attempt number and status.",
        new CounterConfiguration { LabelNames = new[] { "attempt", "status" } });

    public static readonly Histogram RouteDeviationSeconds = Metrics.CreateHistogram(
        "delivery_route_deviation_seconds",
        "Difference between expected ETA and actual delivery time (Actual - Expected).",
        new HistogramConfiguration
        {
            Buckets = new double[] { -900, -600, -300, -120, -60, 0, 60, 120, 300, 600, 900 }
        });

    public static readonly Gauge SignalrActiveConnections = Metrics.CreateGauge(
        "delivery_signalr_active_connections",
        "Current number of active SignalR client connections.",
        new GaugeConfiguration { LabelNames = new[] { "role" } });

    public static readonly Counter GpsAccuracyPointsTotal = Metrics.CreateCounter(
        "delivery_gps_accuracy_points_total",
        "Total number of GPS points received, categorized by accuracy.",
        new CounterConfiguration { LabelNames = new[] { "quality" } });

    public static readonly Counter ClientEventsTotal = Metrics.CreateCounter(
        "delivery_client_events_total",
        "Total number of client telemetry events reported.",
        new CounterConfiguration { LabelNames = new[] { "event_type", "client_type" } });

    public static readonly Counter ClientRouteFallbacksTotal = Metrics.CreateCounter(
        "delivery_client_route_fallbacks_total",
        "Total number of client-side route fallback events.",
        new CounterConfiguration { LabelNames = new[] { "reason", "phase" } });
}
