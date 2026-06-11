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
  final double routeDistanceMeters;
  final double routeDurationSeconds;
  final String? batchGroupId;
  final int batchSequence;
  final int batchSize;
  final List<OrderItemDto> items;
  final DateTime? createdAt;
  final DateTime? assignedAt;
  final DateTime? completedAt;

  const OrderDto({
    required this.id,
    this.status = 'CREATED',
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
    this.routeDistanceMeters = 0,
    this.routeDurationSeconds = 0,
    this.batchGroupId,
    this.batchSequence = 0,
    this.batchSize = 0,
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
          'CREATED',
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
      routeDistanceMeters:
          _toDouble(
            readField(json, 'RouteDistanceMeters') ??
                readField(json, 'routeDistanceMeters'),
          ) ??
          0,
      routeDurationSeconds:
          _toDouble(
            readField(json, 'RouteDurationSeconds') ??
                readField(json, 'routeDurationSeconds'),
          ) ??
          0,
      batchGroupId:
          readField<String>(json, 'BatchGroupId') ??
          readField<String>(json, 'batchGroupId'),
      batchSequence:
          readField<int>(json, 'BatchSequence') ??
          readField<int>(json, 'batchSequence') ??
          0,
      batchSize:
          readField<int>(json, 'BatchSize') ??
          readField<int>(json, 'batchSize') ??
          0,
      items: itemsRaw != null
          ? itemsRaw.map((e) => OrderItemDto.fromJson(Map<String, dynamic>.from(e as Map))).toList()
          : const [],
      createdAt: createdRaw != null ? DateTime.tryParse(createdRaw) : null,
      assignedAt: assignedRaw != null ? DateTime.tryParse(assignedRaw) : null,
      completedAt: completedRaw != null ? DateTime.tryParse(completedRaw) : null,
    );
  }

  Map<String, dynamic> toJson() => {
    'Id': id,
    'Status': status,
    if (pickupLat != null) 'PickupLat': pickupLat,
    if (pickupLng != null) 'PickupLng': pickupLng,
    if (dropoffLat != null) 'DropoffLat': dropoffLat,
    if (dropoffLng != null) 'DropoffLng': dropoffLng,
    if (expectedDeliveryTime != null) 'ExpectedDeliveryTime': expectedDeliveryTime?.toIso8601String(),
    if (assignedRiderId != null) 'AssignedRiderId': assignedRiderId,
    if (customerId != null) 'CustomerId': customerId,
    if (shopId != null) 'ShopId': shopId,
    'DistanceKm': distanceKm,
    'DeliveryFee': deliveryFee,
    if (trackingCode != null) 'TrackingCode': trackingCode,
    if (refNumber != null) 'RefNumber': refNumber,
    if (encodedPolyline != null) 'EncodedPolyline': encodedPolyline,
    'RouteDistanceMeters': routeDistanceMeters,
    'RouteDurationSeconds': routeDurationSeconds,
    if (batchGroupId != null) 'BatchGroupId': batchGroupId,
    'BatchSequence': batchSequence,
    'BatchSize': batchSize,
    'Items': items.map((i) => i.toJson()).toList(),
    if (createdAt != null) 'CreatedAt': createdAt?.toIso8601String(),
    if (assignedAt != null) 'AssignedAt': assignedAt?.toIso8601String(),
    if (completedAt != null) 'CompletedAt': completedAt?.toIso8601String(),
  };
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

  Map<String, dynamic> toJson() => {
    'Id': id,
    'MenuItemId': menuItemId,
    'Name': name,
    'UnitPrice': unitPrice,
    'Quantity': quantity,
    if (notes != null) 'Notes': notes,
    if (optionsDescription != null) 'OptionsDescription': optionsDescription,
    'TotalPrice': totalPrice,
  };
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
  final String customerId;
  final String shopId;
  final List<CreateOrderItemDto> items;

  const CreateOrderDto({
    required this.pickupLat,
    required this.pickupLng,
    required this.dropoffLat,
    required this.dropoffLng,
    required this.expectedDeliveryTime,
    required this.customerId,
    required this.shopId,
    required this.items,
  });

  Map<String, dynamic> toJson() => {
    'PickupLat': pickupLat,
    'PickupLng': pickupLng,
    'DropoffLat': dropoffLat,
    'DropoffLng': dropoffLng,
    'ExpectedDeliveryTime': expectedDeliveryTime.toUtc().toIso8601String(),
    'CustomerId': customerId,
    'ShopId': shopId,
    'Items': items.map((i) => i.toJson()).toList(),
  };
}

class CreateOrderItemDto {
  final String menuItemId;
  final int quantity;
  final String? notes;
  final String? optionsDescription;

  const CreateOrderItemDto({
    required this.menuItemId,
    required this.quantity,
    this.notes,
    this.optionsDescription,
  });

  Map<String, dynamic> toJson() => {
    'MenuItemId': menuItemId,
    'Quantity': quantity,
    if (notes != null) 'Notes': notes,
    if (optionsDescription != null) 'OptionsDescription': optionsDescription,
  };
}
