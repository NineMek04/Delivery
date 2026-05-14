import 'package:freezed_annotation/freezed_annotation.dart';

part 'order.freezed.dart';
part 'order.g.dart';

/// Order DTO — ข้อมูลออเดอร์.
///
/// ตรงกับ:
/// - .NET Entity: `BackendApi/Models/Order.cs`
/// - .NET DTO: `BackendApi/Models/DTOs/OrderDto.cs`
///
/// Fields mapping:
/// | Flutter (Dart)          | .NET (C#)                |
/// |-------------------------|--------------------------|
/// | id                      | Id (GUID string)         |
/// | status                  | Status                   |
/// | pickupLat / pickupLng   | PickupLat / PickupLng    |
/// | dropoffLat / dropoffLng | DropoffLat / DropoffLng  |
/// | expectedDeliveryTime    | ExpectedDeliveryTime     |
/// | assignedRiderId         | AssignedRiderId          |
@freezed
abstract class OrderDto with _$OrderDto {
  const factory OrderDto({
    /// รหัสออเดอร์ (GUID)
    @JsonKey(name: 'Id') required String id,

    /// สถานะ: PENDING, ASSIGNED, PICKED_UP, DELIVERING, COMPLETED, CANCELLED
    @JsonKey(name: 'Status') @Default('PENDING') String status,

    /// ละติจูดจุดรับสินค้า (Pickup)
    @JsonKey(name: 'PickupLat') double? pickupLat,

    /// ลองจิจูดจุดรับสินค้า (Pickup)
    @JsonKey(name: 'PickupLng') double? pickupLng,

    /// ละติจูดจุดส่งสินค้า (Dropoff)
    @JsonKey(name: 'DropoffLat') double? dropoffLat,

    /// ลองจิจูดจุดส่งสินค้า (Dropoff)
    @JsonKey(name: 'DropoffLng') double? dropoffLng,

    /// เวลาที่คาดว่าจะส่งถึง
    @JsonKey(name: 'ExpectedDeliveryTime') DateTime? expectedDeliveryTime,

    /// รหัสไรเดอร์ที่ได้รับมอบหมาย
    @JsonKey(name: 'AssignedRiderId') String? assignedRiderId,
  }) = _OrderDto;

  factory OrderDto.fromJson(Map<String, dynamic> json) =>
      _$OrderDtoFromJson(json);
}

/// Create Order DTO.
///
/// ตรงกับ: `BackendApi/Models/DTOs/CreateOrderDto.cs`
@freezed
abstract class CreateOrderDto with _$CreateOrderDto {
  const factory CreateOrderDto({
    /// ละติจูดจุดรับสินค้า
    @JsonKey(name: 'PickupLat') required double pickupLat,

    /// ลองจิจูดจุดรับสินค้า
    @JsonKey(name: 'PickupLng') required double pickupLng,

    /// ละติจูดจุดส่งสินค้า
    @JsonKey(name: 'DropoffLat') required double dropoffLat,

    /// ลองจิจูดจุดส่งสินค้า
    @JsonKey(name: 'DropoffLng') required double dropoffLng,

    /// เวลาที่คาดว่าจะส่งถึง
    @JsonKey(name: 'ExpectedDeliveryTime') required DateTime expectedDeliveryTime,
  }) = _CreateOrderDto;

  factory CreateOrderDto.fromJson(Map<String, dynamic> json) =>
      _$CreateOrderDtoFromJson(json);
}
