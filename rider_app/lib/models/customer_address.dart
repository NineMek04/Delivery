import '../core/api/api_helpers.dart';

class CustomerAddressDto {
  final String id;
  final String trackingCode;
  final String userId;
  final String name;
  final String addressLine1;
  final String? addressLine2;
  final String city;
  final String state;
  final String postalCode;
  final double latitude;
  final double longitude;
  final bool isDefault;
  final DateTime? createdAt;

  const CustomerAddressDto({
    required this.id,
    this.trackingCode = '',
    this.userId = '',
    required this.name,
    required this.addressLine1,
    this.addressLine2,
    required this.city,
    required this.state,
    required this.postalCode,
    this.latitude = 0.0,
    this.longitude = 0.0,
    this.isDefault = false,
    this.createdAt,
  });

  factory CustomerAddressDto.fromJson(Map<String, dynamic> json) {
    final createdRaw = readField<String>(json, 'CreatedAt') ?? readField<String>(json, 'createdAt');
    
    return CustomerAddressDto(
      id: readField<String>(json, 'Id') ?? readField<String>(json, 'id') ?? '',
      trackingCode: readField<String>(json, 'TrackingCode') ?? readField<String>(json, 'trackingCode') ?? '',
      userId: readField<String>(json, 'UserId') ?? readField<String>(json, 'userId') ?? '',
      name: readField<String>(json, 'Name') ?? readField<String>(json, 'name') ?? '',
      addressLine1: readField<String>(json, 'AddressLine1') ?? readField<String>(json, 'addressLine1') ?? '',
      addressLine2: readField<String>(json, 'AddressLine2') ?? readField<String>(json, 'addressLine2'),
      city: readField<String>(json, 'City') ?? readField<String>(json, 'city') ?? '',
      state: readField<String>(json, 'State') ?? readField<String>(json, 'state') ?? '',
      postalCode: readField<String>(json, 'PostalCode') ?? readField<String>(json, 'postalCode') ?? '',
      latitude: (readField<num>(json, 'Latitude') ?? readField<num>(json, 'latitude') ?? 0.0).toDouble(),
      longitude: (readField<num>(json, 'Longitude') ?? readField<num>(json, 'longitude') ?? 0.0).toDouble(),
      isDefault: readField<bool>(json, 'IsDefault') ?? readField<bool>(json, 'isDefault') ?? false,
      createdAt: createdRaw != null ? DateTime.tryParse(createdRaw) : null,
    );
  }

  Map<String, dynamic> toJson() => {
    'Id': id,
    'TrackingCode': trackingCode,
    'UserId': userId,
    'Name': name,
    'AddressLine1': addressLine1,
    if (addressLine2 != null) 'AddressLine2': addressLine2,
    'City': city,
    'State': state,
    'PostalCode': postalCode,
    'Latitude': latitude,
    'Longitude': longitude,
    'IsDefault': isDefault,
    if (createdAt != null) 'CreatedAt': createdAt?.toIso8601String(),
  };
}
