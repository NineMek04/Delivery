import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';
import '../../../app/app_theme.dart';
import 'animated_route_layer.dart';

class CustomerTrackingMapView extends StatelessWidget {
  final MapController mapController;
  final LatLng? pickupPoint;
  final LatLng? dropoffPoint;
  final LatLng animatedRiderPosition;
  final bool hasRiderPosition;
  final List<LatLng> displayPoints;
  final bool isPickupPhase;
  final Animation<double> routeAnimController;
  final VoidCallback onMapReady;

  const CustomerTrackingMapView({
    super.key,
    required this.mapController,
    required this.pickupPoint,
    required this.dropoffPoint,
    required this.animatedRiderPosition,
    required this.hasRiderPosition,
    required this.displayPoints,
    required this.isPickupPhase,
    required this.routeAnimController,
    required this.onMapReady,
  });

  @override
  Widget build(BuildContext context) {
    return FlutterMap(
      mapController: mapController,
      options: MapOptions(
        initialCenter: pickupPoint ?? dropoffPoint ?? const LatLng(17.4138, 102.7872),
        initialZoom: 14,
        onMapReady: onMapReady,
      ),
      children: [
        TileLayer(
          urlTemplate: kIsWeb
              ? '/map-tiles/{z}/{x}/{y}.png'
              : 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
          userAgentPackageName: 'com.delivery.customer_app',
        ),
        if (displayPoints.isNotEmpty && hasRiderPosition)
          AnimatedRoutePolylineLayer(
            points: displayPoints,
            isPickup: isPickupPhase,
            animation: routeAnimController,
          ),
        MarkerLayer(
          markers: [
            if (pickupPoint != null)
              Marker(
                point: pickupPoint!,
                width: 40,
                height: 40,
                child: const Icon(Icons.store, color: Colors.red, size: 30),
              ),
            if (dropoffPoint != null)
              Marker(
                point: dropoffPoint!,
                width: 40,
                height: 40,
                child: const Icon(Icons.home, color: Colors.blue, size: 30),
              ),
            if (hasRiderPosition)
              Marker(
                point: animatedRiderPosition,
                width: 40,
                height: 40,
                child: const Icon(Icons.delivery_dining, color: AppTheme.primaryColor, size: 35),
              ),
          ],
        ),
      ],
    );
  }
}
