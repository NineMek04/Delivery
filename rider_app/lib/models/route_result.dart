import '../core/api/api_helpers.dart';

/// VRP Route Result from AI Service.
class RouteResult {
  final int vehicleCount;
  final double totalDistance;
  final double totalTime;
  final List<VehicleRoute> routes;

  const RouteResult({
    required this.vehicleCount,
    required this.totalDistance,
    required this.totalTime,
    required this.routes,
  });

  factory RouteResult.fromJson(Map<String, dynamic> json) {
    final rawRoutes = readField<List>(json, 'routes') ?? readField<List>(json, 'Routes') ?? [];
    return RouteResult(
      vehicleCount:
          readField<num>(json, 'vehicleCount')?.toInt() ??
          readField<num>(json, 'VehicleCount')?.toInt() ??
          0,
      totalDistance:
          _toDouble(readField(json, 'totalDistance') ?? readField(json, 'TotalDistance')) ?? 0,
      totalTime:
          _toDouble(readField(json, 'totalTime') ?? readField(json, 'TotalTime')) ?? 0,
      routes: rawRoutes
          .map((e) => VehicleRoute.fromJson(asMap(e)))
          .toList(),
    );
  }
}

class VehicleRoute {
  final int vehicleIndex;
  final List<Waypoint> waypoints;
  final double distance;

  const VehicleRoute({
    required this.vehicleIndex,
    required this.waypoints,
    required this.distance,
  });

  factory VehicleRoute.fromJson(Map<String, dynamic> json) {
    final rawWaypoints =
        readField<List>(json, 'waypoints') ?? readField<List>(json, 'Waypoints') ?? [];
    return VehicleRoute(
      vehicleIndex:
          readField<num>(json, 'vehicleIndex')?.toInt() ??
          readField<num>(json, 'VehicleIndex')?.toInt() ??
          0,
      waypoints: rawWaypoints
          .map((e) => Waypoint.fromJson(asMap(e)))
          .toList(),
      distance:
          _toDouble(readField(json, 'distance') ?? readField(json, 'Distance')) ?? 0,
    );
  }
}

class Waypoint {
  final int sequence;
  final double lat;
  final double lng;
  final String type;
  final String? orderId;
  final String? locationName;

  const Waypoint({
    required this.sequence,
    required this.lat,
    required this.lng,
    required this.type,
    this.orderId,
    this.locationName,
  });

  factory Waypoint.fromJson(Map<String, dynamic> json) {
    return Waypoint(
      sequence:
          readField<num>(json, 'sequence')?.toInt() ??
          readField<num>(json, 'Sequence')?.toInt() ??
          0,
      lat: _toDouble(readField(json, 'lat') ?? readField(json, 'Lat')) ?? 0,
      lng: _toDouble(readField(json, 'lng') ?? readField(json, 'Lng')) ?? 0,
      type:
          readField<String>(json, 'type') ?? readField<String>(json, 'Type') ?? '',
      orderId:
          readField<String>(json, 'orderId') ?? readField<String>(json, 'OrderId'),
      locationName:
          readField<String>(json, 'locationName') ??
          readField<String>(json, 'LocationName'),
    );
  }
}

double? _toDouble(dynamic value) {
  if (value == null) return null;
  if (value is num) return value.toDouble();
  return double.tryParse(value.toString());
}
