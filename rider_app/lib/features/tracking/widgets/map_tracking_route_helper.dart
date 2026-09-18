import 'dart:math' as math;
import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:geolocator/geolocator.dart';
import 'package:latlong2/latlong.dart';

import '../../../models/order.dart';
import '../../../shared/utils/polyline_util.dart';
import 'map_tracking_types.dart';

double calculateBearing(LatLng start, LatLng end) {
  final lat1 = start.latitude * math.pi / 180;
  final lon1 = start.longitude * math.pi / 180;
  final lat2 = end.latitude * math.pi / 180;
  final lon2 = end.longitude * math.pi / 180;

  final dLon = lon2 - lon1;

  final y = math.sin(dLon) * math.cos(lat2);
  final x = math.cos(lat1) * math.sin(lat2) -
      math.sin(lat1) * math.cos(lat2) * math.cos(dLon);

  final radians = math.atan2(y, x);
  return (radians * 180 / math.pi + 360) % 360;
}

List<LatLng> getTailRoute(List<LatLng> fullRoute, LatLng? currentPos) {
  if (currentPos == null || fullRoute.isEmpty) return fullRoute;
  if (fullRoute.length <= 2) return fullRoute;

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

  if (closestIdx >= fullRoute.length - 1) {
    return fullRoute.sublist(fullRoute.length - 2);
  }

  return fullRoute.sublist(closestIdx);
}

double routeDistanceMeters(List<LatLng> points) {
  if (points.length < 2) return 0;
  const distance = Distance();
  var total = 0.0;
  for (var i = 0; i < points.length - 1; i++) {
    total += distance.as(LengthUnit.Meter, points[i], points[i + 1]);
  }
  return total;
}

double distanceToPolyline(LatLng point, List<LatLng> polyline) {
  if (polyline.isEmpty) return double.infinity;
  double minDist = double.infinity;
  for (int i = 0; i < polyline.length; i++) {
    final d = Geolocator.distanceBetween(
      point.latitude,
      point.longitude,
      polyline[i].latitude,
      polyline[i].longitude,
    );
    if (d < minDist) minDist = d;
  }
  return minDist;
}

ResolvedRoute resolveRoute(
  String? encodedPolyline,
  List<LatLng?> fallbackPoints,
) {
  final decoded = encodedPolyline?.isNotEmpty == true
      ? decodePolyline(encodedPolyline!)
      : null;
  if (decoded != null && decoded.length >= 2) {
    return ResolvedRoute(points: decoded);
  }

  final sanitizedFallback = fallbackPoints
      .whereType<LatLng>()
      .toList(growable: false);
  return ResolvedRoute(
    points: sanitizedFallback,
    fallbackReason: encodedPolyline?.isNotEmpty == true
        ? 'INVALID_POLYLINE'
        : 'MISSING_POLYLINE',
    encodedLength: encodedPolyline?.length,
  );
}

Widget buildAnimatedPolylineLayer(List<LatLng> points, {required bool isPickup}) {
  final Color routeColor = isPickup
      ? const Color(0xFFFF9800)
      : const Color(0xFF3B00FF);

  return PolylineLayer(
    polylines: [
      Polyline(
        points: points,
        color: Colors.black.withOpacity(0.34),
        strokeWidth: 10.0,
      ),
      Polyline(
        points: points,
        color: Colors.white.withOpacity(0.95),
        strokeWidth: 8.0,
      ),
      Polyline(
        points: points,
        color: routeColor.withOpacity(0.96),
        strokeWidth: 6.5,
      ),
    ],
  );
}

ResolvedRoute resolveStaticRoute(
  OrderDto order,
  bool isHeadingToPickup,
  String? localRoutePolyline,
  List<LatLng>? localRouteCoordinates,
  String? pickupRoute,
  LatLng? riderPoint,
  LatLng? pickup,
  LatLng? dropoff,
) {
  if (localRouteCoordinates != null && localRouteCoordinates.length >= 2) {
    return ResolvedRoute(points: localRouteCoordinates);
  }

  return isHeadingToPickup
      ? resolveRoute(
          localRoutePolyline ?? pickupRoute,
          [riderPoint, pickup],
        )
      : resolveRoute(
          localRoutePolyline ?? order.encodedPolyline,
          [riderPoint ?? pickup, dropoff],
        );
}
