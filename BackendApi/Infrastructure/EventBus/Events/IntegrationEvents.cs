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
        decimal deliveryFee,
        string? correlationId = null)
        : base(correlationId)
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
    public string? CustomerId { get; init; }

    public OrderStatusChangedIntegrationEvent() { }

    [JsonConstructor]
    public OrderStatusChangedIntegrationEvent(
        string orderId, 
        long refNumber, 
        OrderState oldState, 
        OrderState newState, 
        string? assignedRiderId,
        string? customerId,
        string? correlationId = null)
        : base(correlationId)
    {
        OrderId = orderId;
        RefNumber = refNumber;
        OldState = oldState;
        NewState = newState;
        AssignedRiderId = assignedRiderId;
        CustomerId = customerId;
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

/// <summary>
/// Event published when a rider's presence or online state changes,
/// ensuring durable, out-of-process persistence of rider states.
///
/// [MAGIC-STRING FIX] TargetState and Reason are now strongly-typed enums.
/// RiderTransitionReason.Recover maps to the reconnect-from-STALE flow;
/// the handler resolves the actual RiderState (IDLE/BUSY) from active orders.
/// </summary>
public record RiderStateChangedIntegrationEvent : IntegrationEvent
{
    public string RiderId { get; init; } = null!;

    /// <summary>Target state for direct transitions (STALE, OFFLINE).
    /// For Connect/Recover the handler determines IDLE vs BUSY from active orders.</summary>
    public RiderState? TargetState { get; init; }

    public RiderState? PreviousState { get; init; }

    /// <summary>What triggered this event — replaces the former free-text Reason string.</summary>
    public RiderTransitionReason Reason { get; init; }

    public RiderStateChangedIntegrationEvent() { }

    [JsonConstructor]
    public RiderStateChangedIntegrationEvent(
        string riderId,
        RiderState? targetState,
        RiderState? previousState,
        RiderTransitionReason reason,
        string? correlationId = null)
        : base(correlationId)
    {
        RiderId = riderId;
        TargetState = targetState;
        PreviousState = previousState;
        Reason = reason;
    }
}
