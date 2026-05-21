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
