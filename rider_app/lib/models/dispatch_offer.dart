import '../core/api/api_helpers.dart';

/// Offer payload from SignalR `OfferReceived` event.
class DispatchOffer {
  final String offerId;
  final int version;
  final DateTime? expiresAt;
  final String? riderId;
  final DispatchOfferOrder order;
  final DispatchPickupRoute? pickupRoute;

  const DispatchOffer({
    required this.offerId,
    required this.version,
    this.expiresAt,
    this.riderId,
    required this.order,
    this.pickupRoute,
  });

  factory DispatchOffer.fromJson(Map<String, dynamic> json) {
    final expiresRaw =
        readField<String>(json, 'ExpiresAt') ??
        readField<String>(json, 'expiresAt');

    final orderRaw =
        readField<Map<String, dynamic>>(json, 'Order') ??
        readField<Map<String, dynamic>>(json, 'order');
    final ordersRaw =
        readField<List<dynamic>>(json, 'Orders') ??
        readField<List<dynamic>>(json, 'orders');
    final firstOrderRaw = orderRaw ??
        (ordersRaw != null && ordersRaw.isNotEmpty && ordersRaw.first is Map
            ? Map<String, dynamic>.from(ordersRaw.first as Map)
            : null);

    final routeRaw =
        readField<Map<String, dynamic>>(json, 'PickupRoute') ??
        readField<Map<String, dynamic>>(json, 'pickupRoute');

    return DispatchOffer(
      offerId:
          readField<String>(json, 'OfferId') ??
          readField<String>(json, 'offerId') ??
          '',
      version:
          readField<num>(json, 'Version')?.toInt() ??
          readField<num>(json, 'version')?.toInt() ??
          0,
      expiresAt: expiresRaw != null ? DateTime.tryParse(expiresRaw) : null,
      riderId:
          readField<String>(json, 'RiderId') ?? readField<String>(json, 'riderId'),
      order: firstOrderRaw != null
          ? DispatchOfferOrder.fromJson(firstOrderRaw)
          : const DispatchOfferOrder(id: ''),
      pickupRoute: routeRaw != null
          ? DispatchPickupRoute.fromJson(routeRaw)
          : null,
    );
  }
}

class DispatchOfferOrder {
  final String id;
  final double? pickupLat;
  final double? pickupLng;
  final double? dropoffLat;
  final double? dropoffLng;
  final double? distanceKm;
  final double? deliveryFee;
  final String? encodedPolyline;

  const DispatchOfferOrder({
    required this.id,
    this.pickupLat,
    this.pickupLng,
    this.dropoffLat,
    this.dropoffLng,
    this.distanceKm,
    this.deliveryFee,
    this.encodedPolyline,
  });

  factory DispatchOfferOrder.fromJson(Map<String, dynamic> json) {
    return DispatchOfferOrder(
      id: readField<String>(json, 'Id') ?? readField<String>(json, 'id') ?? '',
      pickupLat: _toDouble(readField(json, 'PickupLat') ?? readField(json, 'pickupLat')),
      pickupLng: _toDouble(readField(json, 'PickupLng') ?? readField(json, 'pickupLng')),
      dropoffLat: _toDouble(readField(json, 'DropoffLat') ?? readField(json, 'dropoffLat')),
      dropoffLng: _toDouble(readField(json, 'DropoffLng') ?? readField(json, 'dropoffLng')),
      distanceKm: _toDouble(readField(json, 'DistanceKm') ?? readField(json, 'distanceKm')),
      deliveryFee: _toDouble(readField(json, 'DeliveryFee') ?? readField(json, 'deliveryFee')),
      encodedPolyline:
          readField<String>(json, 'EncodedPolyline') ??
          readField<String>(json, 'encodedPolyline'),
    );
  }
}

class DispatchPickupRoute {
  final String? encodedPolyline;
  final double? distanceMeters;
  final double? durationSeconds;

  const DispatchPickupRoute({
    this.encodedPolyline,
    this.distanceMeters,
    this.durationSeconds,
  });

  factory DispatchPickupRoute.fromJson(Map<String, dynamic> json) {
    return DispatchPickupRoute(
      encodedPolyline:
          readField<String>(json, 'EncodedPolyline') ??
          readField<String>(json, 'encodedPolyline'),
      distanceMeters: _toDouble(
        readField(json, 'DistanceMeters') ?? readField(json, 'distanceMeters'),
      ),
      durationSeconds: _toDouble(
        readField(json, 'DurationSeconds') ?? readField(json, 'durationSeconds'),
      ),
    );
  }
}

double? _toDouble(dynamic value) {
  if (value == null) return null;
  if (value is num) return value.toDouble();
  return double.tryParse(value.toString());
}
