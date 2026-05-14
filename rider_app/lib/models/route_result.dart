import 'package:freezed_annotation/freezed_annotation.dart';

part 'route_result.freezed.dart';
part 'route_result.g.dart';

/// VRP Route Result — ผลลัพธ์จาก AI Service.
///
/// ตรงกับ:
/// - Python: `ai-engine/main.py` → VRP solver response
///
/// Data Flow:
/// ```
/// Flutter ──► .NET Backend ──► AI Service (OR-Tools)
///                                    │
///                              VRP Result
///                                    │
///                              ◄─── Waypoint Sequence
/// ```
@freezed
abstract class RouteResult with _$RouteResult {
  const factory RouteResult({
    /// จำนวนยานพาหนะที่ใช้
    required int vehicleCount,

    /// ระยะทางรวม (เมตร)
    required double totalDistance,

    /// เวลารวม (วินาที)
    required double totalTime,

    /// เส้นทางของแต่ละยานพาหนะ
    required List<VehicleRoute> routes,
  }) = _RouteResult;

  factory RouteResult.fromJson(Map<String, dynamic> json) =>
      _$RouteResultFromJson(json);
}

/// เส้นทางของยานพาหนะแต่ละคัน.
@freezed
abstract class VehicleRoute with _$VehicleRoute {
  const factory VehicleRoute({
    /// ลำดับที่ของยานพาหนะ
    required int vehicleIndex,

    /// ลำดับจุดแวะพัก (Waypoint Sequence)
    required List<Waypoint> waypoints,

    /// ระยะทางของเส้นทางนี้ (เมตร)
    required double distance,
  }) = _VehicleRoute;

  factory VehicleRoute.fromJson(Map<String, dynamic> json) =>
      _$VehicleRouteFromJson(json);
}

/// Waypoint — จุดแวะพักแต่ละจุดในเส้นทาง.
@freezed
abstract class Waypoint with _$Waypoint {
  const factory Waypoint({
    /// ลำดับในเส้นทาง
    required int sequence,

    /// ละติจูด (SRID 4326)
    required double lat,

    /// ลองจิจูด (SRID 4326)
    required double lng,

    /// ประเภท: pickup / dropoff / depot
    required String type,

    /// รหัสออเดอร์ที่เกี่ยวข้อง (ถ้ามี)
    String? orderId,

    /// ชื่อสถานที่ (ถ้ามี)
    String? locationName,
  }) = _Waypoint;

  factory Waypoint.fromJson(Map<String, dynamic> json) =>
      _$WaypointFromJson(json);
}
