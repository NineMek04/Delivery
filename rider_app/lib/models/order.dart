import '../core/api/api_helpers.dart';

/// Order DTO — maps to BackendApi `OrderDto`.
class OrderDto {
  final String id;
  final String status;
  final double? pickupLat;
  final double? pickupLng;
  final double? dropoffLat;
  final double? dropoffLng;
  final DateTime? expectedDeliveryTime;
  final String? assignedRiderId;
  final double distanceKm;
  final double deliveryFee;
  final String? trackingCode;
  final String? encodedPolyline;

  const OrderDto({
    required this.id,
    this.status = 'PENDING',
    this.pickupLat,
    this.pickupLng,
    this.dropoffLat,
    this.dropoffLng,
    this.expectedDeliveryTime,
    this.assignedRiderId,
    this.distanceKm = 0,
    this.deliveryFee = 0,
    this.trackingCode,
    this.encodedPolyline,
  });

  factory OrderDto.fromJson(Map<String, dynamic> json) {
    final expectedRaw =
        readField<String>(json, 'ExpectedDeliveryTime') ??
        readField<String>(json, 'expectedDeliveryTime');

    return OrderDto(
      id: readField<String>(json, 'Id') ?? readField<String>(json, 'id') ?? '',
      status:
          readField<String>(json, 'Status') ??
          readField<String>(json, 'status') ??
          'PENDING',
      pickupLat: _toDouble(readField(json, 'PickupLat') ?? readField(json, 'pickupLat')),
      pickupLng: _toDouble(readField(json, 'PickupLng') ?? readField(json, 'pickupLng')),
      dropoffLat: _toDouble(readField(json, 'DropoffLat') ?? readField(json, 'dropoffLat')),
      dropoffLng: _toDouble(readField(json, 'DropoffLng') ?? readField(json, 'dropoffLng')),
      expectedDeliveryTime:
          expectedRaw != null ? DateTime.tryParse(expectedRaw) : null,
      assignedRiderId:
          readField<String>(json, 'AssignedRiderId') ??
          readField<String>(json, 'assignedRiderId'),
      distanceKm:
          _toDouble(readField(json, 'DistanceKm') ?? readField(json, 'distanceKm')) ?? 0,
      deliveryFee:
          _toDouble(readField(json, 'DeliveryFee') ?? readField(json, 'deliveryFee')) ?? 0,
      trackingCode:
          readField<String>(json, 'TrackingCode') ??
          readField<String>(json, 'trackingCode'),
      encodedPolyline:
          readField<String>(json, 'EncodedPolyline') ??
          readField<String>(json, 'encodedPolyline'),
    );
  }
}

double? _toDouble(dynamic value) {
  if (value == null) return null;
  if (value is num) return value.toDouble();
  return double.tryParse(value.toString());
}

/// Create Order DTO.
class CreateOrderDto {
  final double pickupLat;
  final double pickupLng;
  final double dropoffLat;
  final double dropoffLng;
  final DateTime expectedDeliveryTime;

  const CreateOrderDto({
    required this.pickupLat,
    required this.pickupLng,
    required this.dropoffLat,
    required this.dropoffLng,
    required this.expectedDeliveryTime,
  });

  Map<String, dynamic> toJson() => {
    'PickupLat': pickupLat,
    'PickupLng': pickupLng,
    'DropoffLat': dropoffLat,
    'DropoffLng': dropoffLng,
    'ExpectedDeliveryTime': expectedDeliveryTime.toIso8601String(),
  };
}
