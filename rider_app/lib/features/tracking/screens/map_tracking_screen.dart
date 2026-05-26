import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:latlong2/latlong.dart' hide Path;
import 'package:geolocator/geolocator.dart';

import '../../../core/signalr/signalr_service.dart';
import '../../../models/dispatch_offer.dart';
import '../../../shared/utils/polyline_util.dart';
import '../../../shared/widgets/connection_status_bar.dart';
import '../../../shared/widgets/error_dialog.dart';
import '../../delivery/providers/delivery_provider.dart';
import '../providers/tracking_provider.dart';

class LatLngTween extends Tween<LatLng> {
  LatLngTween({super.begin, super.end});

  @override
  LatLng lerp(double t) {
    if (begin == null || end == null) return end ?? const LatLng(0, 0);
    final lat = begin!.latitude + (end!.latitude - begin!.latitude) * t;
    final lng = begin!.longitude + (end!.longitude - begin!.longitude) * t;
    return LatLng(lat, lng);
  }
}

class AngleTween extends Tween<double> {
  AngleTween({super.begin, super.end});

  @override
  double lerp(double t) {
    if (begin == null || end == null) return end ?? 0.0;
    double diff = (end! - begin!) % 360;
    if (diff > 180) diff -= 360;
    if (diff < -180) diff += 360;
    return begin! + diff * t;
  }
}

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

  // เก็บค่าล่าสุดสำหรับอนิเมชัน LERP ในแผนที่
  LatLng? _lastAnimatedPoint;
  double? _lastAnimatedHeading;

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

      // กรองแสดงข้อเสนอเฉพาะของไรเดอร์คนปัจจุบัน
      final currentRiderId = ref.read(authServiceProvider.notifier).userId;
      if (currentRiderId == null || offer.riderId != currentRiderId) {
        return;
      }

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

      // กรองสเตตัสเฉพาะออเดอร์ที่เรากำลังทำอยู่ใน Sim Mirror
      final currentShortOrderId = _shortOrder(event.orderId);
      if (_simOrderId != currentShortOrderId) {
        return;
      }

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

      // กรองเฉพาะพิกัดที่เป็นของไรเดอร์คนปัจจุบัน
      final currentRiderId = ref.read(authServiceProvider.notifier).userId;
      if (currentRiderId == null || event.riderId != currentRiderId) {
        return;
      }

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

  List<LatLng> _getTailRoute(List<LatLng> fullRoute, LatLng? currentPos) {
    if (currentPos == null || fullRoute.isEmpty) return fullRoute;
    
    int closestIdx = 0;
    double minDistance = double.infinity;
    
    for (int i = 0; i < fullRoute.length; i++) {
      final dist = Geolocator.distanceBetween(
        currentPos.latitude, currentPos.longitude,
        fullRoute[i].latitude, fullRoute[i].longitude
      );
      if (dist < minDistance) {
        minDistance = dist;
        closestIdx = i;
      }
    }
    
    return fullRoute.sublist(closestIdx);
  }

  Widget _buildNavigationPanel(BuildContext context, LatLng riderPos, dynamic activeOrder, LatLng? pickup, LatLng? dropoff) {
    LatLng? target = pickup;
    String targetName = "จุดรับอาหาร (ร้านค้า)";
    bool isPickup = true;

    if (activeOrder != null) {
      if (activeOrder.state.toString().toUpperCase() == "DELIVERING") {
        target = dropoff;
        targetName = "จุดส่งอาหาร (บ้านลูกค้า)";
        isPickup = false;
      }
    } else if (_simPhase == SimFlowPhase.delivery || _simPhase == SimFlowPhase.completed) {
      target = dropoff;
      targetName = "จุดส่งอาหาร (บ้านลูกค้า)";
      isPickup = false;
    }

    if (target == null) return const SizedBox.shrink();

    final distance = Geolocator.distanceBetween(
      riderPos.latitude, riderPos.longitude,
      target.latitude, target.longitude
    );

    IconData icon;
    String instruction;

    if (distance > 1000) {
      icon = Icons.navigation_outlined;
      instruction = "ตรงไปตามถนนอุดรธานี อีก ${(distance / 1000).toStringAsFixed(1)} กม.";
    } else if (distance > 400) {
      icon = Icons.turn_slight_right;
      instruction = "อีก ${(distance).toStringAsFixed(0)} ม. เตรียมชิดขวาเพื่อเลี้ยว";
    } else if (distance > 100) {
      icon = isPickup ? Icons.store : Icons.turn_right;
      instruction = "อีก ${(distance).toStringAsFixed(0)} ม. เลี้ยวขวาเข้าสู่${isPickup ? 'ร้านค้า' : 'บ้านลูกค้า'}";
    } else {
      icon = Icons.flag;
      instruction = "คุณเดินทางถึง${isPickup ? 'จุดรับอาหาร' : 'จุดส่งมอบอาหาร'}แล้ว!";
    }

    return Card(
      elevation: 6,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      color: Colors.grey[900]?.withOpacity(0.9) ?? Colors.black87,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        child: Row(
          children: [
            Container(
              width: 46,
              height: 46,
              decoration: const BoxDecoration(
                shape: BoxShape.circle,
                color: Color(0xFF1A73E8),
              ),
              child: Icon(icon, color: Colors.white, size: 26),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    instruction,
                    style: const TextStyle(
                      color: Colors.white,
                      fontSize: 15,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    "มุ่งหน้าสู่ $targetName",
                    style: TextStyle(
                      color: Colors.grey[400],
                      fontSize: 12,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
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
    
    // หากสิ้นสุดงานจำลอง (completed หรือ idle) และไม่มีงานจริง ให้เคลียร์จุดหมายและเส้นทางออก
    final isFinished = _simPhase == SimFlowPhase.completed || _simPhase == SimFlowPhase.idle;

    final pickup = order?.pickupLat != null && order?.pickupLng != null
        ? LatLng(order!.pickupLat!, order.pickupLng!)
        : (isFinished ? null : _simPickupPoint);

    final dropoff = order?.dropoffLat != null && order?.dropoffLng != null
        ? LatLng(order!.dropoffLat!, order.dropoffLng!)
        : (isFinished ? null : _simDropoffPoint);

    // คำนวณเส้นทางข้างหลังเพื่อทำการทำ Tail Route Update กราฟิกหดตามเส้นถนนจริง
    final rawRoutePoints = order?.encodedPolyline != null && order!.encodedPolyline!.isNotEmpty
        ? decodePolyline(order.encodedPolyline!)
        : (isFinished
            ? const <LatLng>[]
            : (_simPhase == SimFlowPhase.pickup ? _simPickupRoute : _simDeliveryRoute));
        
    final routePoints = _getTailRoute(rawRoutePoints, riderPoint ?? _simRiderPoint);

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
            child: Stack(
              children: [
                TweenAnimationBuilder<LatLng>(
                  tween: LatLngTween(
                    begin: _lastAnimatedPoint ?? riderPoint ?? _simRiderPoint ?? center,
                    end: riderPoint ?? _simRiderPoint ?? center,
                  ),
                  duration: const Duration(seconds: 1),
                  builder: (context, animatedRiderPoint, child) {
                    _lastAnimatedPoint = animatedRiderPoint;

                    return TweenAnimationBuilder<double>(
                      tween: AngleTween(
                        begin: _lastAnimatedHeading ?? tracking.heading ?? 0.0,
                        end: tracking.heading ?? 0.0,
                      ),
                      duration: const Duration(milliseconds: 500),
                      builder: (context, animatedHeading, child) {
                        _lastAnimatedHeading = animatedHeading;

                        return FlutterMap(
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
                                    point: animatedRiderPoint,
                                    radius: math.max(5.0, tracking.accuracy!),
                                    useRadiusInMeter: true,
                                    color: const Color(0xFF1A73E8).withOpacity(0.16),
                                    borderColor: const Color(0xFF1A73E8).withOpacity(0.36),
                                    borderStrokeWidth: 1.5,
                                  ),
                                ],
                              ),
                            MarkerLayer(
                              markers: [
                                if (_simMirrorEnabled && _simRiderPoint != null)
                                  Marker(
                                    point: animatedRiderPoint,
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
                                    point: animatedRiderPoint,
                                    width: 96,
                                    height: 96,
                                    child: RiderLocationMarker(heading: animatedHeading),
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
                        );
                      },
                    );
                  },
                ),
                // Turn-by-Turn Navigation Panel overlay
                if ((riderPoint != null || _simRiderPoint != null) && (order != null || _simPhase != SimFlowPhase.idle))
                  Positioned(
                    top: 16,
                    left: 16,
                    right: 16,
                    child: _buildNavigationPanel(context, riderPoint ?? _simRiderPoint!, order, pickup, dropoff),
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
