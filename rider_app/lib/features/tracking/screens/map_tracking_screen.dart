import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:latlong2/latlong.dart' hide Path;
import 'package:geolocator/geolocator.dart';
import 'package:url_launcher/url_launcher.dart';
import 'package:sqflite/sqflite.dart';

import '../../../core/api/services/client_route_telemetry_service.dart';
import '../../../core/api/services/rider_route_api_service.dart';
import '../../../core/signalr/signalr_service.dart';
import '../../../core/auth/auth_service.dart';
import '../../../core/location/location_service.dart';
import '../../../core/location/tile_cache_service.dart';
import '../../../core/session/rider_session_service.dart';
import '../../../models/dispatch_offer.dart';
import '../../../models/order.dart';
import '../../../shared/utils/polyline_util.dart';
import '../../../shared/widgets/connection_status_bar.dart';
import '../../../shared/widgets/error_dialog.dart';
import '../../delivery/providers/delivery_provider.dart';

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
  static const double _navigationZoom = 17.5;
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
  String? _dbDir;
  bool _mapReady = false;
  String? _lastViewportSignature;
  String? _lastFollowSignature;
  final Set<String> _reportedRouteFallbacks = <String>{};
  final Set<String> _requestedLocalRoutes = <String>{};
  final Map<String, String> _localRoutePolylines = <String, String>{};

  DateTime? _lastUpdateReceivedTime;
  Duration _animationDuration = const Duration(seconds: 3);
  double? _prevLat;
  double? _prevLng;

  @override
  void initState() {
    super.initState();
    _loadDbDir();
    _bindSimStreams();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      ref.read(deliveryNotifierProvider.notifier).loadOrders();
    });
  }

  Future<void> _loadDbDir() async {
    if (kIsWeb) return;
    try {
      final dbPath = await getDatabasesPath();
      if (mounted) {
        setState(() {
          _dbDir = dbPath;
        });
      }
    } catch (_) {}
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
    _mapController.move(point, _navigationZoom);
  }

  Future<void> _launchMaps(LatLng target, String label) async {
    final googleUrl = Uri.parse(
        'https://www.google.com/maps/dir/?api=1&destination=${target.latitude},${target.longitude}&travelmode=two_wheeler');
    final appleUrl = Uri.parse(
        'maps://?q=${target.latitude},${target.longitude}');

    try {
      if (await canLaunchUrl(googleUrl)) {
        await launchUrl(googleUrl, mode: LaunchMode.externalApplication);
      } else if (await canLaunchUrl(appleUrl)) {
        await launchUrl(appleUrl, mode: LaunchMode.externalApplication);
      } else {
        throw 'Could not launch maps application';
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('ไม่สามารถเปิดแผนที่นำทางได้: $e')),
        );
      }
    }
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

  _ResolvedRoute _resolveRoute(
    String? encodedPolyline,
    List<LatLng?> fallbackPoints,
  ) {
    final decoded = encodedPolyline?.isNotEmpty == true
        ? decodePolyline(encodedPolyline!)
        : const <LatLng>[];
    if (decoded.length >= 2) {
      return _ResolvedRoute(points: decoded);
    }

    return _ResolvedRoute(
      points: fallbackPoints.whereType<LatLng>().toList(growable: false),
      fallbackReason: encodedPolyline?.isNotEmpty == true
          ? 'INVALID_POLYLINE'
          : 'MISSING_POLYLINE',
      encodedLength: encodedPolyline?.length,
    );
  }

  void _reportRouteFallback(
    OrderDto order,
    String routePhase,
    _ResolvedRoute route,
  ) {
    if (route.fallbackReason == null || route.points.length < 2) return;

    final key = '${order.id}|$routePhase|${route.fallbackReason}';
    if (!_reportedRouteFallbacks.add(key)) return;

    Future.microtask(() {
      if (!mounted) return;
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

  String _routeKey(String orderId, String routePhase) {
    return '$orderId|$routePhase';
  }

  void _requestLocalOsrmRoute(
    OrderDto order,
    String routePhase,
    LatLng riderPoint,
    List<LatLng> fallbackPoints,
  ) {
    final key = _routeKey(order.id, routePhase);
    if (_localRoutePolylines.containsKey(key) ||
        !_requestedLocalRoutes.add(key)) {
      return;
    }

    WidgetsBinding.instance.addPostFrameCallback((_) async {
      if (!mounted) return;

      try {
        final route = await ref.read(riderRouteApiServiceProvider).resolve(
              orderId: order.id,
              routePhase: routePhase,
              currentLat: riderPoint.latitude,
              currentLng: riderPoint.longitude,
            );
        final decoded = decodePolyline(route.encodedPolyline);

        if (route.source == 'LOCAL_OSRM' && decoded.length >= 2) {
          if (!mounted) return;
          setState(() {
            _localRoutePolylines[key] = route.encodedPolyline;
            _lastViewportSignature = null;
            _lastFollowSignature = null;
          });
          return;
        }
      } catch (_) {
        // The straight-line fallback remains visible while diagnostics are sent.
      }

      _reportRouteFallback(
        order,
        routePhase,
        _ResolvedRoute(
          points: fallbackPoints,
          fallbackReason: 'LOCAL_OSRM_UNAVAILABLE',
        ),
      );
    });
  }

  void _followRider(LatLng point, String signature) {
    if (!_mapReady || _lastFollowSignature == signature) return;
    _lastFollowSignature = signature;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted || !_mapReady) return;
      try {
        _mapController.move(point, _navigationZoom);
      } catch (_) {}
    });
  }

  void _fitMapToRoute(List<LatLng> points, String signature) {
    if (!_mapReady || points.isEmpty || _lastViewportSignature == signature) {
      return;
    }
    _lastViewportSignature = signature;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted || !_mapReady) return;
      try {
        if (points.length == 1) {
          _mapController.move(points.first, 15);
          return;
        }
        _mapController.fitCamera(
          CameraFit.bounds(
            bounds: LatLngBounds.fromPoints(points),
            padding: const EdgeInsets.fromLTRB(36, 100, 36, 52),
          ),
        );
      } catch (_) {}
    });
  }

  Widget _buildNavigationPanel(
    BuildContext context,
    LatLng riderPos,
    OrderDto? activeOrder,
    LatLng? pickup,
    LatLng? dropoff,
  ) {
    LatLng? target = pickup;
    String targetName = "จุดรับอาหาร (ร้านค้า)";
    bool isPickup = true;

    if (activeOrder != null) {
      if (activeOrder.status.toUpperCase() == "DELIVERING") {
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
            IconButton(
              icon: const Icon(Icons.directions, color: Color(0xFF1A73E8), size: 28),
              tooltip: 'เปิดแผนที่นำทางภายนอก',
              onPressed: () => _launchMaps(target!, targetName),
            ),
          ],
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final tracking = ref.watch(locationServiceProvider);
    final signalR = ref.watch(signalRServiceProvider);
    final delivery = ref.watch(deliveryNotifierProvider);
    final session = ref.watch(riderSessionServiceProvider);

    final riderPoint = tracking.latitude != null && tracking.longitude != null
        ? LatLng(tracking.latitude!, tracking.longitude!)
        : null;

    final activeRiderPoint = riderPoint ?? _simRiderPoint;
    if (activeRiderPoint != null && (activeRiderPoint.latitude != _prevLat || activeRiderPoint.longitude != _prevLng)) {
      _prevLat = activeRiderPoint.latitude;
      _prevLng = activeRiderPoint.longitude;

      final now = DateTime.now();
      if (_lastUpdateReceivedTime != null) {
        final diff = now.difference(_lastUpdateReceivedTime!);
        var seconds = diff.inSeconds;
        if (seconds < 1) seconds = 1;
        if (seconds > 11) seconds = 11;
        _animationDuration = Duration(seconds: seconds);
      } else {
        _animationDuration = const Duration(seconds: 3);
      }
      _lastUpdateReceivedTime = now;
    }

    final order = delivery.activeOrder;

    // หากสิ้นสุดงานจำลอง (completed หรือ idle) และไม่มีงานจริง ให้เคลียร์จุดหมายและเส้นทางออก
    final isFinished = _simPhase == SimFlowPhase.completed || _simPhase == SimFlowPhase.idle;

    final pickup = order?.pickupLat != null && order?.pickupLng != null
        ? LatLng(order!.pickupLat!, order.pickupLng!)
        : (isFinished ? null : _simPickupPoint);

    final dropoff = order?.dropoffLat != null && order?.dropoffLng != null
        ? LatLng(order!.dropoffLat!, order.dropoffLng!)
        : (isFinished ? null : _simDropoffPoint);

    final orderStatus = order?.status.toUpperCase();
    final isHeadingToPickup =
        orderStatus == 'ASSIGNED' || orderStatus == 'PICKING_UP';
    final routePhase = isHeadingToPickup ? 'PICKUP' : 'DELIVERY';
    final pickupRoute = delivery.pickupRouteOrderId == order?.id
        ? delivery.pickupEncodedPolyline
        : null;
    final localRoutePolyline = order == null
        ? null
        : _localRoutePolylines[_routeKey(order.id, routePhase)];
    final resolvedRoute = order != null
        ? (isHeadingToPickup
            ? _resolveRoute(
                localRoutePolyline ?? pickupRoute,
                [riderPoint, pickup],
              )
            : _resolveRoute(
                localRoutePolyline ?? order.encodedPolyline,
                [riderPoint ?? pickup, dropoff],
              ))
        : _ResolvedRoute(
            points: isFinished
                ? const <LatLng>[]
                : (_simPhase == SimFlowPhase.pickup
                    ? _simPickupRoute
                    : _simDeliveryRoute),
          );

    if (order != null &&
        riderPoint != null &&
        resolvedRoute.fallbackReason != null) {
      _requestLocalOsrmRoute(
        order,
        routePhase,
        riderPoint,
        resolvedRoute.points,
      );
    }

    final routePoints = _getTailRoute(
      resolvedRoute.points,
      riderPoint ?? _simRiderPoint,
    );

    final center = riderPoint ?? _simRiderPoint ?? pickup ?? dropoff ?? _defaultCenter;
    final viewportPoints = <LatLng>{
      if (riderPoint != null) riderPoint,
      if (pickup != null) pickup,
      if (dropoff != null) dropoff,
      ...routePoints,
    }.toList(growable: false);
    if (order != null && riderPoint != null) {
      _followRider(
        riderPoint,
        '${order.id}|${riderPoint.latitude.toStringAsFixed(5)}|'
        '${riderPoint.longitude.toStringAsFixed(5)}',
      );
    } else {
      _fitMapToRoute(
        viewportPoints,
        '${order?.id}|$orderStatus|${routePoints.length}|'
        '${riderPoint?.latitude.toStringAsFixed(4)}|'
        '${riderPoint?.longitude.toStringAsFixed(4)}',
      );
    }

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
            isOnline: session.isOnline,
          ),
          Expanded(
            child: Stack(
              children: [
                TweenAnimationBuilder<LatLng>(
                  tween: LatLngTween(
                     begin: _lastAnimatedPoint ?? riderPoint ?? _simRiderPoint ?? center,
                     end: riderPoint ?? _simRiderPoint ?? center,
                  ),
                  duration: _animationDuration,
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
                            onMapReady: () {
                              _mapReady = true;
                              _lastViewportSignature = null;
                              _lastFollowSignature = null;
                              if (order != null && riderPoint != null) {
                                _followRider(
                                  riderPoint,
                                  'ready|${order.id}|'
                                  '${riderPoint.latitude.toStringAsFixed(5)}|'
                                  '${riderPoint.longitude.toStringAsFixed(5)}',
                                );
                              } else {
                                _fitMapToRoute(
                                  viewportPoints,
                                  'ready|${order?.id}|${routePoints.length}',
                                );
                              }
                            },
                          ),
                          children: [
                            TileLayer(
                              urlTemplate: kIsWeb
                                  ? '/map-tiles/{z}/{x}/{y}.png'
                                  : 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                              userAgentPackageName: 'com.delivery.rider_app',
                              tileProvider: !kIsWeb && _dbDir != null
                                  ? CachedTileProvider(dbDir: _dbDir!)
                                  : NetworkTileProvider(),
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
                    tracking.isTracking ? 'GPS Tracking Active' : 'Offline',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  if (tracking.error != null) ...[
                    const SizedBox(height: 8),
                    Text(
                      tracking.error!,
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
                        if (tracking.isTracking) {
                          await ref
                              .read(locationServiceProvider.notifier)
                              .stopTracking();
                        } else {
                          await ref
                              .read(locationServiceProvider.notifier)
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
                      tracking.isTracking ? Icons.gps_off : Icons.gps_fixed,
                    ),
                    label: Text(
                      tracking.isTracking ? 'Stop GPS Tracking' : 'Start GPS Tracking',
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

class _ResolvedRoute {
  const _ResolvedRoute({
    required this.points,
    this.fallbackReason,
    this.encodedLength,
  });

  final List<LatLng> points;
  final String? fallbackReason;
  final int? encodedLength;
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
