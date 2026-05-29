import '../core/api/api_helpers.dart';

/// Rider DTO — maps to BackendApi `RiderDto`.
class RiderDto {
  final String id;
  final String name;
  final String status;
  final double? lat;
  final double? lng;
  final DateTime? lastUpdated;

  const RiderDto({
    required this.id,
    required this.name,
    this.status = 'IDLE',
    this.lat,
    this.lng,
    this.lastUpdated,
  });

  factory RiderDto.fromJson(Map<String, dynamic> json) {
    final lastRaw =
        readField<String>(json, 'LastUpdated') ??
        readField<String>(json, 'lastUpdated');

    return RiderDto(
      id: readField<String>(json, 'Id') ?? readField<String>(json, 'id') ?? '',
      name:
          readField<String>(json, 'Name') ?? readField<String>(json, 'name') ?? '',
      status:
          readField<String>(json, 'Status') ??
          readField<String>(json, 'status') ??
          readField<String>(json, 'State') ??
          readField<String>(json, 'state') ??
          'IDLE',
      lat: _toDouble(readField(json, 'Lat') ?? readField(json, 'lat')),
      lng: _toDouble(readField(json, 'Lng') ?? readField(json, 'lng')),
      lastUpdated: lastRaw != null ? DateTime.tryParse(lastRaw) : null,
    );
  }
}

double? _toDouble(dynamic value) {
  if (value == null) return null;
  if (value is num) return value.toDouble();
  return double.tryParse(value.toString());
}

class CreateRiderDto {
  final String name;
  final double? lat;
  final double? lng;

  const CreateRiderDto({
    required this.name,
    this.lat,
    this.lng,
  });

  Map<String, dynamic> toJson() => {
    'Name': name,
    if (lat != null) 'Lat': lat,
    if (lng != null) 'Lng': lng,
  };
}
