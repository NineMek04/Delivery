import 'dart:math' as math;
import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

import '../../../core/location/tile_cache_service.dart';
import 'map_tracking_markers.dart';
import 'map_tracking_route_helper.dart';

class MapTrackingMapView extends StatelessWidget {
  final MapController mapController;
  final LatLng initialCenter;
  final VoidCallback onMapReady;
  final String? dbDir;
  final List<LatLng> routePoints;
  final bool isNavigatingToDropoff;
  final LatLng? riderPoint;
  final LatLng? simRiderPoint;
  final bool simMirrorEnabled;
  final bool isTracking;
  final double? accuracy;
  final LatLng? pickup;
  final LatLng? dropoff;
  final ValueNotifier<LatLng> animatedPositionNotifier;
  final ValueNotifier<double> animatedHeadingNotifier;

  const MapTrackingMapView({
    super.key,
    required this.mapController,
    required this.initialCenter,
    required this.onMapReady,
    this.dbDir,
    required this.routePoints,
    required this.isNavigatingToDropoff,
    this.riderPoint,
    this.simRiderPoint,
    required this.simMirrorEnabled,
    required this.isTracking,
    this.accuracy,
    this.pickup,
    this.dropoff,
    required this.animatedPositionNotifier,
    required this.animatedHeadingNotifier,
  });

  @override
  Widget build(BuildContext context) {
    return FlutterMap(
      mapController: mapController,
      options: MapOptions(
        initialCenter: initialCenter,
        initialZoom: 14,
        onMapReady: onMapReady,
      ),
      children: [
        TileLayer(
          urlTemplate: kIsWeb
              ? '/map-tiles/{z}/{x}/{y}.png'
              : 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
          userAgentPackageName: 'com.delivery.rider_app',
          tileProvider: !kIsWeb && dbDir != null
              ? CachedTileProvider(dbDir: dbDir!)
              : NetworkTileProvider(),
        ),
        if (routePoints.isNotEmpty)
          buildAnimatedPolylineLayer(
            routePoints,
            isPickup: !isNavigatingToDropoff,
          ),
        if (riderPoint != null && isTracking && accuracy != null)
          ValueListenableBuilder<LatLng>(
            valueListenable: animatedPositionNotifier,
            builder: (context, pos, _) {
              final acc = accuracy ?? 10.0;
              return CircleLayer(
                circles: [
                  CircleMarker(
                    point: pos,
                    radius: math.max(5.0, acc),
                    useRadiusInMeter: true,
                    color: const Color(0xFF1A73E8).withOpacity(0.16),
                    borderColor: const Color(0xFF1A73E8).withOpacity(0.36),
                    borderStrokeWidth: 1.5,
                  ),
                ],
              );
            },
          ),
        ValueListenableBuilder<LatLng>(
          valueListenable: animatedPositionNotifier,
          builder: (context, pos, _) {
            return ValueListenableBuilder<double>(
              valueListenable: animatedHeadingNotifier,
              builder: (context, heading, _) {
                return MarkerLayer(
                  markers: [
                    if (simMirrorEnabled && simRiderPoint != null && riderPoint != null)
                      Marker(
                        point: simRiderPoint!,
                        width: 44,
                        height: 44,
                        child: const Icon(
                          Icons.two_wheeler,
                          color: Colors.purple,
                          size: 34,
                        ),
                      ),
                    if (riderPoint != null || simRiderPoint != null)
                      Marker(
                        point: pos,
                        width: 96,
                        height: 96,
                        child: RiderLocationMarker(heading: heading),
                      ),
                    if (pickup != null)
                      Marker(
                        point: pickup!,
                        width: 40,
                        height: 40,
                        child: const Icon(Icons.store, color: Colors.orange, size: 32),
                      ),
                    if (dropoff != null)
                      Marker(
                        point: dropoff!,
                        width: 40,
                        height: 40,
                        child: const Icon(Icons.home, color: Colors.green, size: 32),
                      ),
                  ],
                );
              },
            );
          },
        ),
      ],
    );
  }
}
