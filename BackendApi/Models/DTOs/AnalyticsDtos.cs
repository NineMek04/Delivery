namespace BackendApi.Models.DTOs;

public class DashboardStatsDto
{
    public int ActiveRiders { get; set; }
    public int IdleRiders { get; set; }
    public int OngoingOrders { get; set; }
    public int PendingOrders { get; set; }
    public int CompletedOrdersToday { get; set; }
    public decimal TotalRevenueToday { get; set; }
}

public class OrderTrendDto
{
    public DateTime Date { get; set; }
    public int TotalOrders { get; set; }
    public int CompletedOrders { get; set; }
}

public class RiderPerformanceDto
{
    public string RiderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CompletedDeliveries { get; set; }
    public double AverageDeliveryTimeMinutes { get; set; }
    public decimal TotalEarned { get; set; }
}

public class AnalyticsSummaryDto
{
    public double AverageDeliveryTimeMinutes { get; set; }
    public double SuccessRatePercent { get; set; }
    public double FailedDispatchPercent { get; set; }
    public int TotalOrdersCount { get; set; }
    public int CompletedOrdersCount { get; set; }
    public int CancelledOrdersCount { get; set; }
}

public class RealtimeTelemetryDto
{
    public int ActiveRidersCount { get; set; }
    public double GpsUpdatesPerSecond { get; set; }
    public int DispatchQueueSize { get; set; }
}

public class RiderUtilizationDto
{
    public int RidersBusyCount { get; set; }
    public int RidersIdleCount { get; set; }
    public int RidersOfflineCount { get; set; }
    public double AverageDeliveriesPerRider { get; set; }
}

public class HeatmapPointDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Intensity { get; set; }
}

