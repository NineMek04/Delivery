import 'package:latlong2/latlong.dart';

List<LatLng> getTailRoute(List<LatLng> fullRoute, LatLng currentPos) {
  if (fullRoute.isEmpty) return [];

  int closestIdx = 0;
  double minDistanceSq = double.infinity;
  final double lat = currentPos.latitude;
  final double lng = currentPos.longitude;

  for (int i = 0; i < fullRoute.length; i++) {
    final p = fullRoute[i];
    final double dLat = lat - p.latitude;
    final double dLng = lng - p.longitude;
    final double distSq = dLat * dLat + dLng * dLng;
    if (distSq < minDistanceSq) {
      minDistanceSq = distSq;
      closestIdx = i;
    }
  }

  return fullRoute.sublist(closestIdx);
}

LatLng? toPoint(double? latitude, double? longitude) {
  if (latitude == null ||
      longitude == null ||
      !latitude.isFinite ||
      !longitude.isFinite ||
      latitude < -90 ||
      latitude > 90 ||
      longitude < -180 ||
      longitude > 180) {
    return null;
  }
  return LatLng(latitude, longitude);
}
