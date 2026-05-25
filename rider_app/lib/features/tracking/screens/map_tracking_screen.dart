import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:latlong2/latlong.dart';

import '../../../core/signalr/signalr_service.dart';
import '../../../models/dispatch_offer.dart';
import '../../../shared/utils/polyline_util.dart';
import '../../../shared/widgets/connection_status_bar.dart';
import '../../../shared/widgets/error_dialog.dart';
import '../../delivery/providers/delivery_provider.dart';
import '../providers/tracking_provider.dart';

class MapTrackingScreen extends ConsumerStatefulWidget {
  const MapTrackingScreen({super.key});

  @override
  ConsumerState<MapTrackingScreen> createState() => _MapTrackingScreenState();
}

class _MapTrackingScreenState extends ConsumerState<MapTrackingScreen> {
  final MapController _mapController = MapController();
  static const _defaultCenter = LatLng(17.4138, 102.7872);
  static const int _maxTimelineItems = 8;

  StreamSubscription<DispatchScanStartedEvent>? _scanSub;
  StreamSubscription<int>? _rankSub;
  StreamSubscription<DispatchOffer>? _offerSub;
  StreamSubscription<OrderStatusChangedEvent>? _statusSub;
  StreamSubscription<RiderLocationUpdateEvent>? _riderLocationSub;

  bool _simMirrorEnabled = false;
  SimFlowPhase _simPhase = SimFlowPhase.idle;
  String _simOrderId = 'WAITING';
  int _simCandidateCount = 0;
  String _simRiderLabel = 'NONE';
  LatLng? _simRiderPoint;
  LatLng? _simPickupPoint;
  LatLng? _simDropoffPoint;
  List<LatLng> _simPickupRoute = const [];
  List<LatLng> _simDeliveryRoute = const [];
  final List<_SimTimelineItem> _timeline = [];

  @override
  void initState() {
    super.initState();
    _bindSimStreams();
  }

  @override
  void dispose() {
    _scanSub?.cancel();
    _rankSub?.cancel();
    _offerSub?.cancel();
    _statusSub?.cancel();
    _riderLocationSub?.cancel();
    _mapController.dispose();
    super.dispose();
  }

  void _centerOn(LatLng? point) {
    if (point == null) return;
    _mapController.move(point, 15);
  }

  void _bindSimStreams() {
    final signalRService = ref.read(signalRServiceProvider.notifier);

    _scanSub = signalRService.onDispatchScanStarted.listen((event) {
      if (!_simMirrorEnabled || !mounted) return;
      setState(() {
        _simPhase = SimFlowPhase.scan;
        _simOrderId = _shortOrder(event.orderId);
        _simCandidateCount = event.nearbyCount;
        _simPickupPoint = _latLngOrNull(event.pickupLat, event.pickupLng);
        _simDropoffPoint = _latLngOrNull(event.dropoffLat, event.dropoffLng);
        _pushTimeline('AI scan started', 'Found ${event.nearbyCount} nearby riders');
      });
      _centerOn(_simPickupPoint ?? _simDropoffPoint);
    });

    _rankSub = signalRService.onDispatchCandidatesRanked.listen((count) {
      if (!_simMirrorEnabled || !mounted) return;
      setState(() {
        _simPhase = SimFlowPhase.offer;
        _simCandidateCount = count;
        _pushTimeline('AI ranking completed', 'Ranked $count rider candidates');
      });
    });

    _offerSub = signalRService.onDispatchOfferSent.listen((offer) {
      if (!_simMirrorEnabled || !mounted) return;
      final riderId = offer.riderId ?? offer.order.id;
      setState(() {
        _simPhase = SimFlowPhase.offer;
        _simOrderId = _shortOrder(offer.order.id);
        _simRiderLabel = _shortRider(riderId);
        _simPickupPoint = _latLngOrNull(offer.order.pickupLat, offer.order.pickupLng);
        _simDropoffPoint =
            _latLngOrNull(offer.order.dropoffLat, offer.order.dropoffLng);
        _simPickupRoute = offer.pickupRoute?.encodedPolyline?.isNotEmpty == true
            ? decodePolyline(offer.pickupRoute!.encodedPolyline!)
            : const [];
        _simDeliveryRoute = offer.order.encodedPolyline?.isNotEmpty == true
            ? decodePolyline(offer.order.encodedPolyline!)
            : const [];
        _pushTimeline('Offer sent', '$_simRiderLabel received order offer');
      });
    });

    _statusSub = signalRService.onOrderStatusChanged.listen((event) {
      if (!_simMirrorEnabled || !mounted) return;
      final nextPhase = _phaseFromStatus(event.status);
      if (nextPhase == null) return;
      setState(() {
        _simPhase = nextPhase;
        _simOrderId = _shortOrder(event.orderId);
        _pushTimeline('Order status', '${_shortOrder(event.orderId)} -> ${event.status}');
      });
    });

    _riderLocationSub = signalRService.onRiderLocationUpdated.listen((event) {
      if (!_simMirrorEnabled || !mounted) return;
      setState(() {
        _simRiderPoint = LatLng(event.latitude, event.longitude);
        _simRiderLabel = _shortRider(event.riderId);
      });
    });
  }

  void _toggleSimMirror() {
    setState(() {
      _simMirrorEnabled = !_simMirrorEnabled;
      if (!_simMirrorEnabled) {
        _simPhase = SimFlowPhase.idle;
        _simOrderId = 'WAITING';
        _simCandidateCount = 0;
        _simRiderLabel = 'NONE';
        _simRiderPoint = null;
        _simPickupPoint = null;
        _simDropoffPoint = null;
        _simPickupRoute = const [];
        _simDeliveryRoute = const [];
        _timeline.clear();
      } else {
        _pushTimeline('Sim mirror enabled', 'Listening to dashboard simulation events');
      }
    });
  }

  void _pushTimeline(String title, String detail) {
    _timeline.insert(
      0,
      _SimTimelineItem(
        title: title,
        detail: detail,
        time: TimeOfDay.now().format(context),
      ),
    );
    if (_timeline.length > _maxTimelineItems) {
      _timeline.removeRange(_maxTimelineItems, _timeline.length);
    }
  }

  SimFlowPhase? _phaseFromStatus(String statusRaw) {
    final status = statusRaw.toUpperCase();
    if (status == 'ASSIGNED') return SimFlowPhase.assigned;
    if (status == 'PICKING_UP') return SimFlowPhase.pickup;
    if (status == 'DELIVERING') return SimFlowPhase.delivery;
    if (status == 'COMPLETED') return SimFlowPhase.completed;
    return null;
  }

  String _shortOrder(String orderId) {
    if (orderId.isEmpty) return 'WAITING';
    return 'ORD-${orderId.substring(0, math.min(6, orderId.length)).toUpperCase()}';
  }

  String _shortRider(String riderId) {
    if (riderId.isEmpty) return 'NONE';
    return 'RID-${riderId.substring(0, math.min(6, riderId.length)).toUpperCase()}';
  }

  LatLng? _latLngOrNull(double? lat, double? lng) {
    if (lat == null || lng == null) return null;
    return LatLng(lat, lng);
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
        : _simPickupPoint;
    final dropoff = order?.dropoffLat != null && order?.dropoffLng != null
        ? LatLng(order!.dropoffLat!, order.dropoffLng!)
        : _simDropoffPoint;

    final routePoints = order?.encodedPolyline != null &&
            order!.encodedPolyline!.isNotEmpty
        ? decodePolyline(order.encodedPolyline!)
        : (_simPhase == SimFlowPhase.pickup ? _simPickupRoute : _simDeliveryRoute);

    final center = riderPoint ?? _simRiderPoint ?? pickup ?? dropoff ?? _defaultCenter;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Map Tracking'),
        actions: [
          IconButton(
            icon: const Icon(Icons.my_location),
            onPressed: () => _centerOn(riderPoint ?? _simRiderPoint ?? center),
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
                if (riderPoint != null &&
                    tracking.isTracking &&
                    tracking.accuracy != null)
                  CircleLayer(
                    circles: [
                      CircleMarker(
                        point: riderPoint,
                        radius: math.max(5.0, tracking.accuracy!),
                        useRadiusInMeter: true,
                        color: const Color(0xFF1A73E8).withValues(alpha: 0.16),
                        borderColor:
                            const Color(0xFF1A73E8).withValues(alpha: 0.36),
                        borderStrokeWidth: 1.5,
                      ),
                    ],
                  ),
                MarkerLayer(
                  markers: [
                    if (_simMirrorEnabled && _simRiderPoint != null)
                      Marker(
                        point: _simRiderPoint!,
                        width: 44,
                        height: 44,
                        child: const Icon(
                          Icons.two_wheeler,
                          color: Colors.purple,
                          size: 34,
                        ),
                      ),
                    if (riderPoint != null)
                      Marker(
                        point: riderPoint,
                        width: 96,
                        height: 96,
                        child: RiderLocationMarker(heading: tracking.heading),
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
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          _simMirrorEnabled ? 'SIM MIRROR: ON' : 'SIM MIRROR: OFF',
                          style: Theme.of(context).textTheme.titleSmall,
                        ),
                      ),
                      TextButton.icon(
                        onPressed: _toggleSimMirror,
                        icon: Icon(
                          _simMirrorEnabled ? Icons.pause_circle : Icons.play_circle,
                        ),
                        label: Text(_simMirrorEnabled ? 'Stop' : 'Start'),
                      ),
                    ],
                  ),
                  if (_simMirrorEnabled) ...[
                    Text(
                      'Phase: ${_simPhase.label} | Order: $_simOrderId | Rider: $_simRiderLabel | Scan: $_simCandidateCount',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                    const SizedBox(height: 8),
                    SizedBox(
                      height: 80,
                      child: ListView.builder(
                        itemCount: _timeline.length,
                        itemBuilder: (context, index) {
                          final item = _timeline[index];
                          return Text(
                            '${item.time}  ${item.title}: ${item.detail}',
                            style: Theme.of(context).textTheme.bodySmall,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                          );
                        },
                      ),
                    ),
                    const Divider(height: 20),
                  ],
                  Text(
                    tracking.isOnline ? 'GPS Tracking Active' : 'Offline',
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
                      tracking.accuracy != null
                          ? 'Position: ${riderPoint.latitude.toStringAsFixed(5)}, ${riderPoint.longitude.toStringAsFixed(5)} | +/-${tracking.accuracy!.toStringAsFixed(0)}m'
                          : 'Position: ${riderPoint.latitude.toStringAsFixed(5)}, ${riderPoint.longitude.toStringAsFixed(5)}',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  const SizedBox(height: 12),
                  ElevatedButton.icon(
                    onPressed: () async {
                      try {
                        if (tracking.isOnline) {
                          await ref
                              .read(trackingNotifierProvider.notifier)
                              .stopTracking();
                        } else {
                          await ref
                              .read(trackingNotifierProvider.notifier)
                              .startTracking();
                        }
                      } catch (e) {
                        if (!context.mounted) return;
                        ErrorDialog.show(
                          context,
                          title: 'GPS',
                          message: e.toString(),
                        );
                      }
                    },
                    icon: Icon(
                      tracking.isOnline ? Icons.gps_off : Icons.gps_fixed,
                    ),
                    label: Text(
                      tracking.isOnline ? 'Stop GPS Tracking' : 'Start GPS Tracking',
                    ),
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

enum SimFlowPhase {
  idle('waiting'),
  scan('scan'),
  offer('offer'),
  assigned('assigned'),
  pickup('pickup'),
  delivery('delivery'),
  completed('completed');

  const SimFlowPhase(this.label);
  final String label;
}

class _SimTimelineItem {
  const _SimTimelineItem({
    required this.title,
    required this.detail,
    required this.time,
  });

  final String title;
  final String detail;
  final String time;
}

class RiderLocationMarker extends StatelessWidget {
  const RiderLocationMarker({super.key, this.heading});

  final double? heading;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 96,
      height: 96,
      child: Stack(
        alignment: Alignment.center,
        children: [
          if (heading != null)
            Transform.rotate(
              angle: heading! * math.pi / 180,
              child: CustomPaint(
                size: const Size(76, 76),
                painter: HeadingConePainter(
                  color: const Color(0xFF1A73E8).withValues(alpha: 0.32),
                ),
              ),
            ),
          Container(
            width: 34,
            height: 34,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: const Color(0xFF1A73E8).withValues(alpha: 0.18),
              boxShadow: [
                BoxShadow(
                  color: const Color(0xFF1A73E8).withValues(alpha: 0.42),
                  blurRadius: 18,
                  spreadRadius: 7,
                ),
              ],
            ),
          ),
          Container(
            width: 22,
            height: 22,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: const Color(0xFF1A73E8),
              border: Border.all(color: const Color(0xFFF8FAFC), width: 3),
            ),
          ),
        ],
      ),
    );
  }
}

class HeadingConePainter extends CustomPainter {
  const HeadingConePainter({required this.color});

  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final path = Path()
      ..moveTo(center.dx, 2)
      ..lineTo(center.dx - 22, center.dy + 8)
      ..quadraticBezierTo(
        center.dx,
        center.dy + 18,
        center.dx + 22,
        center.dy + 8,
      )
      ..close();

    canvas.drawPath(
      path,
      Paint()
        ..color = color
        ..style = PaintingStyle.fill,
    );
  }

  @override
  bool shouldRepaint(covariant HeadingConePainter oldDelegate) {
    return oldDelegate.color != color;
  }
}
