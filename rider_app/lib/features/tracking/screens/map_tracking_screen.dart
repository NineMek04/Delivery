import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:latlong2/latlong.dart' hide Path;
import 'package:sqflite/sqflite.dart';

import '../../../core/auth/auth_service.dart';
import '../../../core/location/location_service.dart';
import '../../../core/session/rider_session_service.dart';
import '../../../core/signalr/signalr_service.dart';
import '../../../models/order.dart';
import '../../../shared/utils/order_status_helper.dart';
import '../../../shared/widgets/connection_status_bar.dart';
import '../../../shared/widgets/error_dialog.dart';
import '../../delivery/providers/delivery_provider.dart';
import '../widgets/map_tracking_animator.dart';
import '../widgets/map_tracking_map_view.dart';
import '../widgets/map_tracking_osrm_manager.dart';
import '../widgets/map_tracking_overlays.dart';
import '../widgets/map_tracking_route_helper.dart';
import '../widgets/map_tracking_sim_handler.dart';
import '../widgets/map_tracking_types.dart';

class MapTrackingScreen extends ConsumerStatefulWidget {
  const MapTrackingScreen({super.key});

  @override
  ConsumerState<MapTrackingScreen> createState() => _MapTrackingScreenState();
}

class _MapTrackingScreenState extends ConsumerState<MapTrackingScreen>
    with TickerProviderStateMixin, AutomaticKeepAliveClientMixin {
  @override
  bool get wantKeepAlive => true;

  final MapController _mapController = MapController();
  static const _defaultCenter = LatLng(17.4138, 102.7872);
  static const double _navigationZoom = 17.5;

  final MapTrackingSimHandler _simHandler = MapTrackingSimHandler();
  final MapTrackingOsrmManager _osrmManager = MapTrackingOsrmManager();
  late final MapTrackingAnimator _animator;

  int _offRouteCount = 0;
  bool _soundEnabled = true;
  String? _dbDir;
  bool _mapReady = false;
  String? _lastViewportSignature;
  String? _lastFollowSignature;

  @override
  void initState() {
    super.initState();
    _animator = MapTrackingAnimator(vsync: this);
    _loadDbDir();
    _bindStreams();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      ref.read(deliveryNotifierProvider.notifier).loadOrders();
    });
  }

  Future<void> _loadDbDir() async {
    if (kIsWeb) return;
    try {
      final dbPath = await getDatabasesPath();
      if (mounted) setState(() => _dbDir = dbPath);
    } catch (_) {}
  }

  void _bindStreams() {
    _simHandler.bindStreams(
      signalRService: ref.read(signalRServiceProvider.notifier),
      currentRiderId: ref.read(authServiceProvider.notifier).userId,
      assignedRiderId: ref.read(deliveryNotifierProvider).activeOrder?.assignedRiderId,
      onCurrentRiderLocation: (lat, lng) {
        ref.read(locationServiceProvider.notifier).updateStatePosition(lat, lng, 0.0);
      },
      onCenter: _centerOn,
      onNotify: () {
        if (mounted) setState(() {});
      },
    );
  }

  @override
  void dispose() {
    _simHandler.dispose();
    _animator.dispose();
    _mapController.dispose();
    super.dispose();
  }

  void _centerOn(LatLng? point) {
    if (point == null) return;
    _mapController.move(point, _navigationZoom);
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
    if (!_mapReady || points.isEmpty || _lastViewportSignature == signature) return;
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

  Future<void> _advanceActiveOrder(OrderDto order) async {
    final nextStatus = OrderStatusHelper.nextRiderStatus(order.status);
    if (nextStatus == null) return;

    await ref.read(deliveryNotifierProvider.notifier).updateOrderStatus(order.id, nextStatus);
    if (!mounted) return;

    final error = ref.read(deliveryNotifierProvider).error;
    if (error != null && !error.startsWith('Offline:')) {
      await ErrorDialog.show(
        context,
        title: 'อัปเดตสถานะไม่สำเร็จ',
        message: error,
      );
      return;
    }

    setState(() {
      _osrmManager.clearOrderRoutes(order.id);
      _lastViewportSignature = null;
      _lastFollowSignature = null;
    });
  }

  @override
  Widget build(BuildContext context) {
    super.build(context);
    final tracking = ref.watch(locationServiceProvider);
    final signalR = ref.watch(signalRServiceProvider);
    final delivery = ref.watch(deliveryNotifierProvider);
    final session = ref.watch(riderSessionServiceProvider);

    final riderPoint = tracking.latitude != null && tracking.longitude != null
        ? LatLng(tracking.latitude!, tracking.longitude!)
        : null;

    final activeRiderPoint = riderPoint ?? _simHandler.simRiderPoint;
    _animator.updateRiderPoint(activeRiderPoint, tracking.heading);

    final order = delivery.activeOrder;
    final isFinished = _simHandler.simPhase == SimFlowPhase.completed || _simHandler.simPhase == SimFlowPhase.idle;

    final pickup = order?.pickupLat != null && order?.pickupLng != null
        ? LatLng(order!.pickupLat!, order.pickupLng!)
        : (isFinished ? null : _simHandler.simPickupPoint);

    final dropoff = order?.dropoffLat != null && order?.dropoffLng != null
        ? LatLng(order!.dropoffLat!, order.dropoffLng!)
        : (isFinished ? null : _simHandler.simDropoffPoint);

    final orderStatus = order?.status.toUpperCase();
    final isNavigatingToDropoff = orderStatus == 'DELIVERING';
    final routePhase = isNavigatingToDropoff ? 'DELIVERY' : 'PICKUP';
    final pickupRoute = delivery.pickupRouteOrderId == order?.id ? delivery.pickupEncodedPolyline : null;
    final localRoutePolyline = order == null ? null : _osrmManager.localRoutePolylines[_osrmManager.routeKey(order.id, routePhase)];
    final localRouteCoordinates = order == null ? null : _osrmManager.localRouteCoordinates[_osrmManager.routeKey(order.id, routePhase)];
    final nextStatus = order == null ? null : OrderStatusHelper.nextRiderStatus(order.status);
    final nextActionLabel = order == null || nextStatus == null ? null : OrderStatusHelper.nextActionLabel(order.status);

    final ResolvedRoute resolvedRoute;
    if (order != null) {
      resolvedRoute = resolveStaticRoute(
        order,
        !isNavigatingToDropoff,
        localRoutePolyline,
        localRouteCoordinates,
        pickupRoute,
        riderPoint,
        pickup,
        dropoff,
      );
    } else {
      resolvedRoute = ResolvedRoute(
        points: isFinished ? const <LatLng>[] : (_simHandler.simPhase == SimFlowPhase.pickup ? _simHandler.simPickupRoute : _simHandler.simDeliveryRoute),
      );
    }

    final hasResolvedRoadRoute = localRoutePolyline?.isNotEmpty == true || (localRouteCoordinates?.length ?? 0) >= 2;
    if (order != null && riderPoint != null && !hasResolvedRoadRoute) {
      _osrmManager.requestLocalOsrmRoute(
        ref: ref,
        order: order,
        routePhase: routePhase,
        riderPoint: riderPoint,
        fallbackPoints: resolvedRoute.points,
        onRouteResolved: () {
          if (!mounted) return;
          setState(() {
            _lastViewportSignature = null;
            _lastFollowSignature = null;
          });
        },
      );
    }

    if (order != null && riderPoint != null && resolvedRoute.points.length >= 2) {
      final offRouteDistance = distanceToPolyline(riderPoint, resolvedRoute.points);
      if (offRouteDistance > 50.0) {
        _offRouteCount++;
        if (_offRouteCount >= 2) {
          _offRouteCount = 0;
          _osrmManager.invalidateRoute(order.id, routePhase);
          _osrmManager.requestLocalOsrmRoute(
            ref: ref,
            order: order,
            routePhase: routePhase,
            riderPoint: riderPoint,
            fallbackPoints: resolvedRoute.points,
            onRouteResolved: () {
              if (mounted) setState(() {});
            },
          );
        }
      } else {
        _offRouteCount = 0;
      }
    }

    final routePoints = getTailRoute(resolvedRoute.points, riderPoint ?? _simHandler.simRiderPoint);
    final routePolylineDistance = routePoints.length >= 2 ? routeDistanceMeters(routePoints) : null;
    final displayRouteDistance = routePolylineDistance ?? _osrmManager.routeDistance;
    final persistedDeliveryDuration = order != null && order.routeDurationSeconds > 0 ? order.routeDurationSeconds : null;
    final displayRouteDuration = _osrmManager.routeDuration ?? persistedDeliveryDuration;

    final center = riderPoint ?? _simHandler.simRiderPoint ?? pickup ?? dropoff ?? _defaultCenter;
    final viewportPoints = <LatLng>{
      if (riderPoint != null) riderPoint,
      if (pickup != null) pickup,
      if (dropoff != null) dropoff,
      ...routePoints,
    }.toList(growable: false);

    if (order != null && riderPoint != null) {
      _followRider(riderPoint, '${order.id}|${riderPoint.latitude.toStringAsFixed(5)}|${riderPoint.longitude.toStringAsFixed(5)}');
    } else {
      _fitMapToRoute(viewportPoints, '${order?.id}|$orderStatus|${routePoints.length}|${riderPoint?.latitude.toStringAsFixed(4)}|${riderPoint?.longitude.toStringAsFixed(4)}');
    }

    return Scaffold(
      appBar: AppBar(
        title: const Text('Map Tracking'),
        actions: [
          IconButton(
            icon: const Icon(Icons.my_location),
            onPressed: () => _centerOn(riderPoint ?? _simHandler.simRiderPoint ?? center),
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
                MapTrackingMapView(
                  mapController: _mapController,
                  initialCenter: center,
                  onMapReady: () {
                    _mapReady = true;
                    _lastViewportSignature = null;
                    _lastFollowSignature = null;
                    if (order != null && riderPoint != null) {
                      _followRider(riderPoint, 'ready|${order.id}|${riderPoint.latitude.toStringAsFixed(5)}|${riderPoint.longitude.toStringAsFixed(5)}');
                    } else {
                      _fitMapToRoute(viewportPoints, 'ready|${order?.id}|${routePoints.length}');
                    }
                  },
                  dbDir: _dbDir,
                  routePoints: routePoints,
                  isNavigatingToDropoff: isNavigatingToDropoff,
                  riderPoint: riderPoint,
                  simRiderPoint: _simHandler.simRiderPoint,
                  simMirrorEnabled: _simHandler.simMirrorEnabled,
                  isTracking: tracking.isTracking,
                  accuracy: tracking.accuracy,
                  pickup: pickup,
                  dropoff: dropoff,
                  animatedPositionNotifier: _animator.positionNotifier,
                  animatedHeadingNotifier: _animator.headingNotifier,
                ),
                MapTrackingOverlays(
                  riderPoint: riderPoint,
                  simRiderPoint: _simHandler.simRiderPoint,
                  order: order,
                  simPhase: _simHandler.simPhase,
                  pickup: pickup,
                  dropoff: dropoff,
                  isNavigatingToDropoff: isNavigatingToDropoff,
                  displayRouteDistance: displayRouteDistance,
                  displayRouteDuration: displayRouteDuration,
                  soundEnabled: _soundEnabled,
                  isUpdating: delivery.isUpdating,
                  nextActionLabel: nextActionLabel,
                  onCenter: () => _centerOn(riderPoint),
                  onOverview: () {
                    _lastViewportSignature = null;
                    if (order != null) {
                      _fitMapToRoute(viewportPoints, 'manual|${order.id}|${routePoints.length}');
                    }
                  },
                  onToggleSound: () => setState(() => _soundEnabled = !_soundEnabled),
                  onAdvanceOrder: nextStatus == null
                      ? null
                      : () {
                          unawaited(_advanceActiveOrder(order!));
                        },
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
