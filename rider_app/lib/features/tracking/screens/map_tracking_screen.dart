import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:latlong2/latlong.dart';

import '../../../core/signalr/signalr_service.dart';
import '../../../shared/utils/polyline_util.dart';
import '../../../shared/widgets/connection_status_bar.dart';
import '../../../shared/widgets/error_dialog.dart';
import '../../delivery/providers/delivery_provider.dart';
import '../providers/tracking_provider.dart';

/// แผนที่ OpenStreetMap + GPS + เส้นทางออเดอร์.
class MapTrackingScreen extends ConsumerStatefulWidget {
  const MapTrackingScreen({super.key});

  @override
  ConsumerState<MapTrackingScreen> createState() => _MapTrackingScreenState();
}

class _MapTrackingScreenState extends ConsumerState<MapTrackingScreen> {
  final MapController _mapController = MapController();
  static const _defaultCenter = LatLng(17.4138, 102.7872);

  @override
  void dispose() {
    _mapController.dispose();
    super.dispose();
  }

  void _centerOn(LatLng? point) {
    if (point == null) return;
    _mapController.move(point, 15);
  }

  @override
  Widget build(BuildContext context) {
    final tracking = ref.watch(trackingNotifierProvider);
    final signalR = ref.watch(signalRServiceProvider);
    final delivery = ref.watch(deliveryNotifierProvider);

    final riderPoint = tracking.latitude != null && tracking.longitude != null
        ? LatLng(tracking.latitude!, tracking.longitude!)
        : null;

    final order = delivery.activeOrder;
    final pickup = order?.pickupLat != null && order?.pickupLng != null
        ? LatLng(order!.pickupLat!, order.pickupLng!)
        : null;
    final dropoff = order?.dropoffLat != null && order?.dropoffLng != null
        ? LatLng(order!.dropoffLat!, order.dropoffLng!)
        : null;

    final routePoints = order?.encodedPolyline != null &&
            order!.encodedPolyline!.isNotEmpty
        ? decodePolyline(order.encodedPolyline!)
        : <LatLng>[];

    final center = riderPoint ?? pickup ?? dropoff ?? _defaultCenter;

    return Scaffold(
      appBar: AppBar(
        title: const Text('แผนที่'),
        actions: [
          IconButton(
            icon: const Icon(Icons.my_location),
            onPressed: () => _centerOn(riderPoint ?? center),
          ),
        ],
      ),
      body: Column(
        children: [
          ConnectionStatusBar(
            signalRState: signalR,
            isGpsTracking: tracking.isTracking,
            isOnline: tracking.isOnline,
          ),
          Expanded(
            child: FlutterMap(
              mapController: _mapController,
              options: MapOptions(
                initialCenter: center,
                initialZoom: 14,
              ),
              children: [
                TileLayer(
                  urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                  userAgentPackageName: 'com.delivery.rider_app',
                ),
                if (routePoints.isNotEmpty)
                  PolylineLayer(
                    polylines: [
                      Polyline(
                        points: routePoints,
                        color: Theme.of(context).colorScheme.primary,
                        strokeWidth: 4,
                      ),
                    ],
                  ),
                MarkerLayer(
                  markers: [
                    if (riderPoint != null)
                      Marker(
                        point: riderPoint,
                        width: 44,
                        height: 44,
                        child: const Icon(Icons.two_wheeler, color: Colors.blue, size: 36),
                      ),
                    if (pickup != null)
                      Marker(
                        point: pickup,
                        width: 40,
                        height: 40,
                        child: const Icon(Icons.store, color: Colors.orange, size: 32),
                      ),
                    if (dropoff != null)
                      Marker(
                        point: dropoff,
                        width: 40,
                        height: 40,
                        child: const Icon(Icons.home, color: Colors.green, size: 32),
                      ),
                  ],
                ),
              ],
            ),
          ),
          Card(
            margin: const EdgeInsets.all(16),
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    tracking.isOnline ? 'กำลังติดตาม GPS' : 'ออฟไลน์',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  if (tracking.locationError != null) ...[
                    const SizedBox(height: 8),
                    Text(
                      tracking.locationError!,
                      style: TextStyle(color: Theme.of(context).colorScheme.error),
                    ),
                  ],
                  if (riderPoint != null)
                    Text(
                      'ตำแหน่ง: ${riderPoint.latitude.toStringAsFixed(5)}, ${riderPoint.longitude.toStringAsFixed(5)}',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  const SizedBox(height: 12),
                  ElevatedButton.icon(
                    onPressed: () async {
                      try {
                        if (tracking.isOnline) {
                          await ref.read(trackingNotifierProvider.notifier).stopTracking();
                        } else {
                          await ref.read(trackingNotifierProvider.notifier).startTracking();
                        }
                      } catch (e) {
                        if (context.mounted) {
                          ErrorDialog.show(
                            context,
                            title: 'GPS',
                            message: e.toString(),
                          );
                        }
                      }
                    },
                    icon: Icon(tracking.isOnline ? Icons.gps_off : Icons.gps_fixed),
                    label: Text(tracking.isOnline ? 'หยุดติดตาม' : 'เริ่มติดตาม GPS'),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
