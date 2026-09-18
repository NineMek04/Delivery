/// Order status change from SignalR `OrderStatusChanged`.
class OrderStatusChangedEvent {
  final String orderId;
  final String status;

  const OrderStatusChangedEvent({
    required this.orderId,
    required this.status,
  });
}

/// Result from `OfferAcceptedResult` hub callback.
class OfferAcceptedResult {
  final bool success;
  final String? message;

  const OfferAcceptedResult({required this.success, this.message});
}

/// Result from `RiderStatusUpdatedResult` hub callback.
class RiderStatusResult {
  final bool success;
  final String? status;
  final String? message;

  const RiderStatusResult({
    required this.success,
    this.status,
    this.message,
  });
}

/// Rider location event used by simulation mirror UI and real-time route updates.
class RiderLocationUpdateEvent {
  final String riderId;
  final double latitude;
  final double longitude;
  final String status;
  final DateTime timestamp;
  final String? snappedPolyline;
  final double? routeDistance;
  final double? routeDuration;

  const RiderLocationUpdateEvent({
    required this.riderId,
    required this.latitude,
    required this.longitude,
    required this.status,
    required this.timestamp,
    this.snappedPolyline,
    this.routeDistance,
    this.routeDuration,
  });
}

/// Dispatch scan start event used by simulation mirror UI.
class DispatchScanStartedEvent {
  final String orderId;
  final double? pickupLat;
  final double? pickupLng;
  final double? dropoffLat;
  final double? dropoffLng;
  final int nearbyCount;

  const DispatchScanStartedEvent({
    required this.orderId,
    required this.pickupLat,
    required this.pickupLng,
    required this.dropoffLat,
    required this.dropoffLng,
    required this.nearbyCount,
  });
}
