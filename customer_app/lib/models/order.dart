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
  final String? customerId;
  final String? shopId;
  final double distanceKm;
  final double deliveryFee;
  final String? trackingCode;
  final long? refNumber;
  final String? encodedPolyline;
  final List<OrderItemDto> items;
  final DateTime? createdAt;
  final DateTime? assignedAt;
  final DateTime? completedAt;

  const OrderDto({
    required this.id,
    this.status = 'PENDING',
    this.pickupLat,
    this.pickupLng,
    this.dropoffLat,
    this.dropoffLng,
    this.expectedDeliveryTime,
    this.assignedRiderId,
    this.customerId,
    this.shopId,
    this.distanceKm = 0,
    this.deliveryFee = 0,
    this.trackingCode,
    this.refNumber,
    this.encodedPolyline,
    this.items = const [],
    this.createdAt,
    this.assignedAt,
    this.completedAt,
  });

  factory OrderDto.fromJson(Map<String, dynamic> json) {
    final expectedRaw =
        readField<String>(json, 'ExpectedDeliveryTime') ??
        readField<String>(json, 'expectedDeliveryTime');
    
    final createdRaw =
        readField<String>(json, 'CreatedAt') ??
        readField<String>(json, 'createdAt');
    
    final assignedRaw =
        readField<String>(json, 'AssignedAt') ??
        readField<String>(json, 'assignedAt');

    final completedRaw =
        readField<String>(json, 'CompletedAt') ??
        readField<String>(json, 'completedAt');

    final itemsRaw = readField<List>(json, 'Items') ?? readField<List>(json, 'items');

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
      customerId:
          readField<String>(json, 'CustomerId') ??
          readField<String>(json, 'customerId'),
      shopId:
          readField<String>(json, 'ShopId') ??
          readField<String>(json, 'shopId'),
      distanceKm:
          _toDouble(readField(json, 'DistanceKm') ?? readField(json, 'distanceKm')) ?? 0,
      deliveryFee:
          _toDouble(readField(json, 'DeliveryFee') ?? readField(json, 'deliveryFee')) ?? 0,
      trackingCode:
          readField<String>(json, 'TrackingCode') ??
          readField<String>(json, 'trackingCode'),
      refNumber: readField<long>(json, 'RefNumber') ?? readField<long>(json, 'refNumber'),
      encodedPolyline:
          readField<String>(json, 'EncodedPolyline') ??
          readField<String>(json, 'encodedPolyline'),
      items: itemsRaw != null
          ? itemsRaw.map((e) => OrderItemDto.fromJson(Map<String, dynamic>.from(e as Map))).toList()
          : const [],
      createdAt: createdRaw != null ? DateTime.tryParse(createdRaw) : null,
      assignedAt: assignedRaw != null ? DateTime.tryParse(assignedRaw) : null,
      completedAt: completedRaw != null ? DateTime.tryParse(completedRaw) : null,
    );
  }
}

class OrderItemDto {
  final String id;
  final String menuItemId;
  final String name;
  final double unitPrice;
  final int quantity;
  final String? notes;
  final String? optionsDescription;
  final double totalPrice;

  const OrderItemDto({
    required this.id,
    required this.menuItemId,
    required this.name,
    this.unitPrice = 0,
    this.quantity = 0,
    this.notes,
    this.optionsDescription,
    this.totalPrice = 0,
  });

  factory OrderItemDto.fromJson(Map<String, dynamic> json) {
    return OrderItemDto(
      id: readField<String>(json, 'Id') ?? readField<String>(json, 'id') ?? '',
      menuItemId: readField<String>(json, 'MenuItemId') ?? readField<String>(json, 'menuItemId') ?? '',
      name: readField<String>(json, 'Name') ?? readField<String>(json, 'name') ?? '',
      unitPrice: _toDouble(readField(json, 'UnitPrice') ?? readField(json, 'unitPrice')) ?? 0,
      quantity: readField<int>(json, 'Quantity') ?? readField<int>(json, 'quantity') ?? 0,
      notes: readField<String>(json, 'Notes') ?? readField<String>(json, 'notes'),
      optionsDescription: readField<String>(json, 'OptionsDescription') ?? readField<String>(json, 'optionsDescription'),
      totalPrice: _toDouble(readField(json, 'TotalPrice') ?? readField(json, 'totalPrice')) ?? 0,
    );
  }
}

double? _toDouble(dynamic value) {
  if (value == null) return null;
  if (value is num) return value.toDouble();
  return double.tryParse(value.toString());
}

typedef long = int;

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
