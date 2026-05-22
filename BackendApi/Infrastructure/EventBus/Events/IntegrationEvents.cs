using System.Text.Json.Serialization;
using BackendApi.Core.StateMachines;

namespace BackendApi.Infrastructure.EventBus.Events;

/// <summary>
/// Event published when a new order is successfully created in the system.
/// </summary>
public record OrderCreatedIntegrationEvent : IntegrationEvent
{
    public string OrderId { get; init; } = null!;
    public long RefNumber { get; init; }
    public OrderState State { get; init; }
    public double PickupLatitude { get; init; }
    public double PickupLongitude { get; init; }
    public double DropoffLatitude { get; init; }
    public double DropoffLongitude { get; init; }
    public double DistanceKm { get; init; }
    public decimal DeliveryFee { get; init; }

    public OrderCreatedIntegrationEvent() { }

    [JsonConstructor]
    public OrderCreatedIntegrationEvent(
        string orderId, 
        long refNumber, 
        OrderState state, 
        double pickupLatitude, 
        double pickupLongitude, 
        double dropoffLatitude, 
        double dropoffLongitude, 
        double distanceKm, 
        decimal deliveryFee)
    {
        OrderId = orderId;
        RefNumber = refNumber;
        State = state;
        PickupLatitude = pickupLatitude;
        PickupLongitude = pickupLongitude;
        DropoffLatitude = dropoffLatitude;
        DropoffLongitude = dropoffLongitude;
        DistanceKm = distanceKm;
        DeliveryFee = deliveryFee;
    }
}

/// <summary>
/// Event published when an order transitions to a new state in its lifecycle.
/// </summary>
public record OrderStatusChangedIntegrationEvent : IntegrationEvent
{
    public string OrderId { get; init; } = null!;
    public long RefNumber { get; init; }
    public OrderState OldState { get; init; }
    public OrderState NewState { get; init; }
    public string? AssignedRiderId { get; init; }

    public OrderStatusChangedIntegrationEvent() { }

    [JsonConstructor]
    public OrderStatusChangedIntegrationEvent(
        string orderId, 
        long refNumber, 
        OrderState oldState, 
        OrderState newState, 
        string? assignedRiderId)
    {
        OrderId = orderId;
        RefNumber = refNumber;
        OldState = oldState;
        NewState = newState;
        AssignedRiderId = assignedRiderId;
    }
}

/// <summary>
/// Event published when a rider updates their GPS coordinates.
/// </summary>
public record RiderLocationUpdatedIntegrationEvent : IntegrationEvent
{
    public string RiderId { get; init; } = null!;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double? Speed { get; init; }
    public double? Heading { get; init; }
    public DateTime Timestamp { get; init; }

    public RiderLocationUpdatedIntegrationEvent() { }

    [JsonConstructor]
    public RiderLocationUpdatedIntegrationEvent(
        string riderId, 
        double latitude, 
        double longitude, 
        double? speed, 
        double? heading, 
        DateTime timestamp)
    {
        RiderId = riderId;
        Latitude = latitude;
        Longitude = longitude;
        Speed = speed;
        Heading = heading;
        Timestamp = timestamp;
    }
}
