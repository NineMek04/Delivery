import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:latlong2/latlong.dart';
import '../../shared/utils/polyline_util.dart';
import '../../core/api/services/rider_route_api_service.dart';
import '../../shared/widgets/order_review_dialog.dart';
import '../delivery/screens/chat_screen.dart';
import 'providers/tracking_provider.dart';
import 'widgets/customer_tracking_details.dart';
import 'widgets/customer_tracking_map_view.dart';
import 'widgets/customer_tracking_route_helper.dart';

class CustomerTrackingScreen extends ConsumerStatefulWidget {
  final String orderId;

  const CustomerTrackingScreen({super.key, required this.orderId});

  @override
  ConsumerState<CustomerTrackingScreen> createState() => _CustomerTrackingScreenState();
}

class _CustomerTrackingScreenState extends ConsumerState<CustomerTrackingScreen> with TickerProviderStateMixin {
  final MapController _mapController = MapController();
  List<LatLng> _routePoints = [];
  bool _fetchingRoute = false;
  String? _lastRoutePhase;
  bool _isRouteResolved = false;
  DateTime? _lastFetchTime;
  LatLng? _lastAnimatedRiderPoint;
  DateTime? _lastUpdateReceivedTime;
  Duration _animationDuration = const Duration(seconds: 5);
  String? _lastSnappedPolyline;
  double? _prevRiderLat;
  double? _prevRiderLng;
  bool _hasPromptedReview = false;

  bool _mapReady = false;
  String? _lastFollowSignature;
  late final AnimationController _positionAnimController;
  late final AnimationController _routeAnimController;
  LatLng _animatedRiderPosition = const LatLng(17.4138, 102.7872);
  LatLng? _animTargetPosition;

  @override
  void initState() {
    super.initState();
    final activeOrder = ref.read(activeOrderProvider);
    if (activeOrder.riderLat != null && activeOrder.riderLng != null) {
      _animatedRiderPosition = LatLng(activeOrder.riderLat!, activeOrder.riderLng!);
    }
    _positionAnimController = AnimationController(
      vsync: this,
      duration: _animationDuration,
    )..addListener(_onPositionAnimTick);

    _routeAnimController = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 2),
    )..repeat();
    Future.microtask(() => ref.read(activeOrderProvider.notifier).watchOrder(widget.orderId));
  }

  void _onPositionAnimTick() {
    if (_animTargetPosition == null || _lastAnimatedRiderPoint == null) return;
    final t = _positionAnimController.value;
    final begin = _lastAnimatedRiderPoint!;
    final end = _animTargetPosition!;
    final lat = begin.latitude + (end.latitude - begin.latitude) * t;
    final lng = begin.longitude + (end.longitude - begin.longitude) * t;
    setState(() {
      _animatedRiderPosition = LatLng(lat, lng);
    });
  }

  void _onRiderPositionChanged(LatLng newPoint) {
    final now = DateTime.now();
    if (_lastUpdateReceivedTime != null) {
      final diff = now.difference(_lastUpdateReceivedTime!);
      var seconds = diff.inSeconds;
      if (seconds < 1) seconds = 1;
      if (seconds > 6) seconds = 6;
      _animationDuration = Duration(seconds: seconds);
    } else {
      _animationDuration = const Duration(seconds: 5);
    }
    _lastUpdateReceivedTime = now;

    _lastAnimatedRiderPoint = _animatedRiderPosition;
    _animTargetPosition = newPoint;
    _positionAnimController.duration = _animationDuration;
    _positionAnimController.forward(from: 0.0);
  }

  void _followRider(LatLng point, String signature) {
    if (!_mapReady || _lastFollowSignature == signature) return;
    _lastFollowSignature = signature;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted || !_mapReady) return;
      try {
        _mapController.move(point, 15.0);
      } catch (_) {}
    });
  }

  @override
  void didUpdateWidget(covariant CustomerTrackingScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.orderId != widget.orderId) {
      setState(() {
        _routePoints = [];
        _lastRoutePhase = null;
        _isRouteResolved = false;
        _lastFetchTime = null;
        _lastAnimatedRiderPoint = null;
        _lastUpdateReceivedTime = null;
        _animationDuration = const Duration(seconds: 5);
        _lastSnappedPolyline = null;
        _prevRiderLat = null;
        _prevRiderLng = null;
        _animTargetPosition = null;
      });
      _positionAnimController.stop();
      Future.microtask(
        () => ref
            .read(activeOrderProvider.notifier)
            .watchOrder(widget.orderId),
      );
    }
  }

  @override
  void dispose() {
    _positionAnimController.dispose();
    _routeAnimController.dispose();
    _mapController.dispose();
    super.dispose();
  }

  Future<void> _updateRoutePoints(String orderId, String status, double? riderLat, double? riderLng) async {
    if (riderLat == null || riderLng == null) return;
    
    final routePhase = (status == 'ASSIGNED' || status == 'PICKING_UP') ? 'PICKUP' : (status == 'DELIVERING' ? 'DELIVERY' : null);
    if (routePhase == null) {
      if (_routePoints.isNotEmpty) {
        setState(() {
          _routePoints = [];
          _isRouteResolved = false;
        });
      }
      return;
    }

    final now = DateTime.now();
    if (_fetchingRoute) return;

    final needsFetch = !_isRouteResolved || _lastRoutePhase != routePhase;
    if (!needsFetch) return;

    if (!_isRouteResolved && _lastFetchTime != null && now.difference(_lastFetchTime!).inSeconds < 10) {
      return;
    }

    _fetchingRoute = true;
    _lastFetchTime = now;
    _lastRoutePhase = routePhase;

    try {
      final route = await ref.read(riderRouteApiServiceProvider).resolve(
        orderId: orderId,
        routePhase: routePhase,
        currentLat: riderLat,
        currentLng: riderLng,
      );
      final pts = decodePolyline(route.encodedPolyline);
      
      if (mounted) {
        setState(() {
          _routePoints = pts;
          _isRouteResolved = pts.length >= 2;
        });
      }
    } catch (_) {
      _isRouteResolved = false;
      final pickupPoint = toPoint(
        ref.read(activeOrderProvider).order?.pickupLat,
        ref.read(activeOrderProvider).order?.pickupLng,
      );
      final dropoffPoint = toPoint(
        ref.read(activeOrderProvider).order?.dropoffLat,
        ref.read(activeOrderProvider).order?.dropoffLng,
      );
      final dest = routePhase == 'PICKUP' ? pickupPoint : dropoffPoint;
      if (dest != null && mounted) {
        setState(() {
          _routePoints = [LatLng(riderLat, riderLng), dest];
        });
      }
    } finally {
      _fetchingRoute = false;
    }
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(activeOrderProvider);
    final pickupPoint = toPoint(
      state.order?.pickupLat,
      state.order?.pickupLng,
    );
    final dropoffPoint = toPoint(
      state.order?.dropoffLat,
      state.order?.dropoffLng,
    );

    if (state.snappedPolyline != null && state.snappedPolyline != _lastSnappedPolyline) {
      _lastSnappedPolyline = state.snappedPolyline;
      final pts = decodePolyline(state.snappedPolyline!);
      if (pts.length >= 2) {
        _routePoints = pts;
        _isRouteResolved = true;
      }
    }

    if (state.riderLat != null && state.riderLng != null &&
        (state.riderLat != _prevRiderLat || state.riderLng != _prevRiderLng)) {
      _prevRiderLat = state.riderLat;
      _prevRiderLng = state.riderLng;
      final newPoint = LatLng(state.riderLat!, state.riderLng!);
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted) return;
        _onRiderPositionChanged(newPoint);
      });
    }

    if (state.riderLat != null && state.riderLng != null && state.order != null) {
      final riderLatLng = LatLng(state.riderLat!, state.riderLng!);
      _followRider(
        riderLatLng,
        '${state.order!.id}|${state.riderLat!.toStringAsFixed(5)}|'
        '${state.riderLng!.toStringAsFixed(5)}',
      );
    }

    if (state.order != null) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        _updateRoutePoints(
          state.order!.id,
          state.order!.status,
          state.riderLat,
          state.riderLng,
        );
      });
    }

    if (state.order?.status == 'COMPLETED' && !_hasPromptedReview && state.order?.rating == null) {
      _hasPromptedReview = true;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted && state.order != null) {
          OrderReviewDialog.show(
            context,
            order: state.order!,
            onReviewed: () {
              ref.read(activeOrderProvider.notifier).watchOrder(widget.orderId);
            },
          );
        }
      });
    }

    return Scaffold(
      appBar: AppBar(
        title: Column(
          children: [
            const Text('ติดตามออเดอร์'),
            if (state.order != null)
              Text(
                state.order!.trackingCode ?? state.order!.id.substring(0, 8),
                style: const TextStyle(fontSize: 12, fontWeight: FontWeight.normal),
              ),
          ],
        ),
        actions: [
          if (state.order != null)
            IconButton(
              icon: const Icon(Icons.chat_outlined),
              tooltip: 'แชทกับไรเดอร์',
              onPressed: () {
                Navigator.of(context).push(
                  MaterialPageRoute(
                    builder: (context) => ChatScreen(
                      orderId: widget.orderId,
                      initialStatus: state.order?.status,
                    ),
                  ),
                );
              },
            ),
        ],
      ),
      body: state.isLoading
          ? const Center(child: CircularProgressIndicator())
          : state.error != null
              ? Center(child: Text(state.error!))
              : state.order == null
                  ? const Center(child: Text('ไม่พบข้อมูลออเดอร์'))
                  : Column(
                      children: [
                        // Map Section
                        Expanded(
                          flex: 3,
                          child: Builder(
                            builder: (context) {
                              final bool useLiveRoute = state.snappedPolyline != null && state.snappedPolyline!.isNotEmpty;
                              final List<LatLng> displayPoints = useLiveRoute
                                  ? _routePoints
                                  : getTailRoute(_routePoints, _animatedRiderPosition);
                              final bool isPickupPhase = !(state.order?.status == 'DELIVERING');
                              return CustomerTrackingMapView(
                                mapController: _mapController,
                                pickupPoint: pickupPoint,
                                dropoffPoint: dropoffPoint,
                                animatedRiderPosition: _animatedRiderPosition,
                                hasRiderPosition: state.riderLat != null && state.riderLng != null,
                                displayPoints: displayPoints,
                                isPickupPhase: isPickupPhase,
                                routeAnimController: _routeAnimController,
                                onMapReady: () => setState(() => _mapReady = true),
                              );
                            },
                          ),
                        ),
                        // Details Section
                        Expanded(
                          flex: 2,
                          child: CustomerTrackingDetailsSheet(
                            orderId: widget.orderId,
                            order: state.order!,
                            routeDuration: state.routeDuration,
                            routeDistance: state.routeDistance,
                          ),
                        ),
                      ],
                    ),
    );
  }
}
