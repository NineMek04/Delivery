import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:latlong2/latlong.dart';

import '../../../core/api/services/client_route_telemetry_service.dart';
import '../../../core/api/services/rider_route_api_service.dart';
import '../../../models/order.dart';
import '../../../shared/utils/polyline_util.dart';
import 'map_tracking_types.dart';

class MapTrackingOsrmManager {
  final Set<String> reportedRouteFallbacks = <String>{};
  final Set<String> requestedLocalRoutes = <String>{};
  final Map<String, String> localRoutePolylines = <String, String>{};
  final Map<String, List<LatLng>> localRouteCoordinates = <String, List<LatLng>>{};

  double? routeDistance;
  double? routeDuration;

  String routeKey(String orderId, String routePhase) => '$orderId|$routePhase';

  void clearOrderRoutes(String orderId) {
    localRoutePolylines.removeWhere((key, _) => key.startsWith('$orderId|'));
    localRouteCoordinates.removeWhere((key, _) => key.startsWith('$orderId|'));
    requestedLocalRoutes.removeWhere((key) => key.startsWith('$orderId|'));
    routeDistance = null;
    routeDuration = null;
  }

  void invalidateRoute(String orderId, String routePhase) {
    final key = routeKey(orderId, routePhase);
    localRoutePolylines.remove(key);
    localRouteCoordinates.remove(key);
    requestedLocalRoutes.remove(key);
  }

  void reportRouteFallback(
    WidgetRef ref,
    OrderDto order,
    String routePhase,
    ResolvedRoute route,
  ) {
    if (route.fallbackReason == null || route.points.length < 2) return;

    final key = '${order.id}|$routePhase|${route.fallbackReason}';
    if (!reportedRouteFallbacks.add(key)) return;

    Future.microtask(() {
      unawaited(
        ref.read(clientRouteTelemetryServiceProvider).reportFallback(
              orderId: order.id,
              routePhase: routePhase,
              reason: route.fallbackReason!,
              encodedLength: route.encodedLength,
            ),
      );
    });
  }

  void _releaseRouteRequestAfterCooldown(String key) {
    Future.delayed(const Duration(seconds: 10), () {
      requestedLocalRoutes.remove(key);
    });
  }

  void requestLocalOsrmRoute({
    required WidgetRef ref,
    required OrderDto order,
    required String routePhase,
    required LatLng riderPoint,
    required List<LatLng> fallbackPoints,
    required VoidCallback onRouteResolved,
  }) {
    final key = routeKey(order.id, routePhase);
    if (localRoutePolylines.containsKey(key) ||
        localRouteCoordinates.containsKey(key) ||
        !requestedLocalRoutes.add(key)) {
      return;
    }

    WidgetsBinding.instance.addPostFrameCallback((_) async {
      try {
        final route = await ref.read(riderRouteApiServiceProvider).resolve(
              orderId: order.id,
              routePhase: routePhase,
              currentLat: riderPoint.latitude,
              currentLng: riderPoint.longitude,
            );
        final decoded = decodePolyline(route.encodedPolyline);
        final routePoints = decoded.length >= 2 ? decoded : route.coordinates;
        debugPrint(
          '[LOCAL_OSRM] source=${route.source} '
          'encoded=${route.encodedPolyline.length} '
          'decoded=${decoded.length} '
          'coordinates=${route.coordinates.length} '
          'distance=${route.distanceMeters}',
        );

        if (routePoints.length >= 2) {
          if (route.encodedPolyline.isNotEmpty) {
            localRoutePolylines[key] = route.encodedPolyline;
          }
          localRouteCoordinates[key] = routePoints;
          routeDistance = route.distanceMeters;
          routeDuration = route.durationSeconds;
          onRouteResolved();
          return;
        }
        _releaseRouteRequestAfterCooldown(key);
        reportRouteFallback(
          ref,
          order,
          routePhase,
          ResolvedRoute(
            points: fallbackPoints,
            fallbackReason: route.encodedPolyline.isEmpty
                ? 'LOCAL_OSRM_UNAVAILABLE'
                : 'INVALID_POLYLINE',
            encodedLength: route.encodedPolyline.length,
          ),
        );
        return;
      } catch (_) {
        _releaseRouteRequestAfterCooldown(key);
      }

      reportRouteFallback(
        ref,
        order,
        routePhase,
        ResolvedRoute(
          points: fallbackPoints,
          fallbackReason: 'LOCAL_OSRM_UNAVAILABLE',
        ),
      );
    });
  }
}
