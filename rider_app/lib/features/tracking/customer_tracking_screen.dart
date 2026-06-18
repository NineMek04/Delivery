import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';
import 'package:latlong2/latlong.dart';
import '../../app/app_theme.dart';
import '../../shared/utils/order_status_helper.dart';
import '../../core/api/services/rider_route_api_service.dart';
import 'services/simulated_journey_service.dart';
import 'providers/tracking_provider.dart';

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
    // Repeating controller drives the flowing dashed line animation (marching ants)
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

  /// Builds an animated dashed polyline layer driven by [_routeAnimController].
  /// Simulates the "marching ants" flowing dash effect from the test-dashboard.
  /// [isPickup] = true → orange dashes (heading to store)
  ///              false → cyan dashes (delivering to customer)
  Widget _buildAnimatedPolylineLayer(List<LatLng> points, {required bool isPickup}) {
    const double dashLength = 20.0;
    const double gapLength = 12.0;
    const double totalPattern = dashLength + gapLength;
    final Color routeColor = isPickup
        ? const Color(0xFFFF9800)   // orange — pickup
        : const Color(0xFF00E5FF);  // cyan   — delivery

    return AnimatedBuilder(
      animation: _routeAnimController,
      builder: (context, _) {
        // Shift the dash/gap lengths to create a sliding offset illusion.
        final double offset = _routeAnimController.value * totalPattern;
        // segments alternates [dash, gap, dash, gap...].
        // Leading dash of 'offset' shifts the phase; the trailing full cycle
        // ensures the pattern tiles seamlessly.
        final List<double> segments = [
          offset,       // leading partial dash (phase shift)
          gapLength,
          dashLength,
          gapLength,
        ];
        return PolylineLayer(
          polylines: [
            Polyline(
              points: points,
              color: routeColor.withValues(alpha: 0.90),
              strokeWidth: 5.0,
              pattern: StrokePattern.dashed(
                segments: segments,
                patternFit: PatternFit.extendFinalDash,
              ),
            ),
            // Faded base line for depth/contrast
            Polyline(
              points: points,
              color: routeColor.withValues(alpha: 0.20),
              strokeWidth: 5.0,
            ),
          ],
        );
      },
    );
  }

  List<LatLng> _getTailRoute(List<LatLng> fullRoute, LatLng currentPos) {
    if (fullRoute.isEmpty) return [];
    
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

    // Throttle retries to at least 10 seconds if we are currently showing fallback
    if (!_isRouteResolved && _lastFetchTime != null && now.difference(_lastFetchTime!).inSeconds < 10) {
      return;
    }

    _fetchingRoute = true;
    _lastFetchTime = now;
    _lastRoutePhase = routePhase;

    try {
      final simService = ref.read(simulatedJourneyProvider);
      final route = await ref.read(riderRouteApiServiceProvider).resolve(
        orderId: orderId,
        routePhase: routePhase,
        currentLat: riderLat,
        currentLng: riderLng,
      );
      final pts = simService.decodePolyline(route.encodedPolyline);
      
      if (mounted) {
        setState(() {
          _routePoints = pts;
          _isRouteResolved = pts.length >= 2;
        });
      }
    } catch (_) {
      // Fallback
      _isRouteResolved = false;
      final pickupPoint = _toPoint(
        ref.read(activeOrderProvider).order?.pickupLat,
        ref.read(activeOrderProvider).order?.pickupLng,
      );
      final dropoffPoint = _toPoint(
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
    final pickupPoint = _toPoint(
      state.order?.pickupLat,
      state.order?.pickupLng,
    );
    final dropoffPoint = _toPoint(
      state.order?.dropoffLat,
      state.order?.dropoffLng,
    );

    // Apply real-time snapped polyline if received from SignalR
    if (state.snappedPolyline != null && state.snappedPolyline != _lastSnappedPolyline) {
      _lastSnappedPolyline = state.snappedPolyline;
      final pts = ref.read(simulatedJourneyProvider).decodePolyline(state.snappedPolyline!);
      if (pts.length >= 2) {
        _routePoints = pts;
        _isRouteResolved = true;
      }
    }

    // Measure dynamic interval when coordinates change
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
                              return FlutterMap(
                                mapController: _mapController,
                                options: MapOptions(
                                  initialCenter: pickupPoint ??
                                      dropoffPoint ??
                                      const LatLng(17.4138, 102.7872),
                                  initialZoom: 14,
                                  onMapReady: () {
                                    setState(() => _mapReady = true);
                                  },
                                ),
                                children: [
                                  TileLayer(
                                    urlTemplate: kIsWeb
                                        ? '/map-tiles/{z}/{x}/{y}.png'
                                        : 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                                    userAgentPackageName: 'com.delivery.customer_app',
                                  ),
                                  if (_routePoints.isNotEmpty && state.riderLat != null && state.riderLng != null)
                                    Builder(
                                      builder: (context) {
                                        final bool useLiveRoute = state.snappedPolyline != null && state.snappedPolyline!.isNotEmpty;
                                        final List<LatLng> displayPoints = useLiveRoute
                                            ? _routePoints
                                            : _getTailRoute(_routePoints, _animatedRiderPosition);
                                        // Determine phase: PICKUP (to store) or DELIVERY (to customer)
                                        final bool isPickupPhase = !(state.order?.status == 'DELIVERING');
                                        return _buildAnimatedPolylineLayer(
                                          displayPoints,
                                          isPickup: isPickupPhase,
                                        );
                                      }
                                    ),
                                  MarkerLayer(
                                    markers: [
                                      // Store Marker
                                      if (pickupPoint != null)
                                        Marker(
                                          point: pickupPoint,
                                          width: 40,
                                          height: 40,
                                          child: const Icon(
                                            Icons.store,
                                            color: Colors.red,
                                            size: 30,
                                          ),
                                        ),
                                      // Customer Marker
                                      if (dropoffPoint != null)
                                        Marker(
                                          point: dropoffPoint,
                                          width: 40,
                                          height: 40,
                                          child: const Icon(
                                            Icons.home,
                                            color: Colors.blue,
                                            size: 30,
                                          ),
                                        ),
                                      // Rider Marker (if available)
                                      if (state.riderLat != null && state.riderLng != null)
                                        Marker(
                                          point: _animatedRiderPosition,
                                          width: 40,
                                          height: 40,
                                          child: const Icon(Icons.delivery_dining, color: AppTheme.primaryColor, size: 35),
                                        ),
                                    ],
                                  ),
                                ],
                              );
                            },
                          ),
                        ),
                        // Details Section
                        Expanded(
                          flex: 2,
                          child: Container(
                            padding: const EdgeInsets.all(16),
                            decoration: BoxDecoration(
                              color: Colors.white,
                              boxShadow: [
                                BoxShadow(
                                  color: Colors.black.withOpacity(0.05),
                                  blurRadius: 10,
                                  offset: const Offset(0, -5),
                                ),
                              ],
                              borderRadius: const BorderRadius.vertical(top: Radius.circular(24)),
                            ),
                            child: SingleChildScrollView(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  if (state.routeDuration != null || state.routeDistance != null) ...[
                                    Container(
                                      width: double.infinity,
                                      padding: const EdgeInsets.all(12),
                                      decoration: BoxDecoration(
                                        color: Colors.blue[50],
                                        borderRadius: BorderRadius.circular(12),
                                        border: Border.all(color: Colors.blue[100]!),
                                      ),
                                      child: Row(
                                        children: [
                                          const Icon(Icons.timer, color: AppTheme.primaryColor),
                                          const SizedBox(width: 8),
                                          Expanded(
                                            child: Text(
                                              'ไรเดอร์กำลังนำส่ง! จะถึงในประมาณ ${_formatDuration(state.routeDuration)} (${_formatDistance(state.routeDistance)})',
                                              style: const TextStyle(fontWeight: FontWeight.bold, color: Colors.blueAccent),
                                            ),
                                          ),
                                        ],
                                      ),
                                    ),
                                    const SizedBox(height: 12),
                                  ],
                                  _OrderProgressBar(status: state.order!.status),
                                  const Divider(height: 24),
                                  const Text(
                                    'รายการอาหาร',
                                    style: TextStyle(fontWeight: FontWeight.bold, fontSize: 16),
                                  ),
                                  const SizedBox(height: 12),
                                  ...state.order!.items.map((item) => Padding(
                                    padding: const EdgeInsets.only(bottom: 8),
                                    child: Row(
                                      children: [
                                        Container(
                                          padding: const EdgeInsets.all(6),
                                          decoration: BoxDecoration(color: Colors.grey[100], borderRadius: BorderRadius.circular(8)),
                                          child: Text('${item.quantity}x', style: const TextStyle(fontWeight: FontWeight.bold)),
                                        ),
                                        const SizedBox(width: 12),
                                        Expanded(child: Text(item.name)),
                                        Text(NumberFormat.currency(locale: 'th', symbol: '฿', decimalDigits: 0).format(item.totalPrice)),
                                      ],
                                    ),
                                  )),
                                  const Divider(height: 32),
                                  Row(
                                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                                    children: [
                                      const Text('ยอดรวมทั้งหมด', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 18)),
                                      Text(
                                        NumberFormat.currency(locale: 'th', symbol: '฿', decimalDigits: 0).format(state.order!.deliveryFee + state.order!.items.fold(0.0, (sum, item) => sum + item.totalPrice)),
                                        style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18, color: AppTheme.primaryColor),
                                      ),
                                    ],
                                  ),
                                  const SizedBox(height: 16),
                                ],
                              ),
                            ),
                          ),
                        ),
                      ],
                    ),
    );
  }

  static LatLng? _toPoint(double? latitude, double? longitude) {
    if (latitude == null ||
        longitude == null ||
        !latitude.isFinite ||
        !longitude.isFinite ||
        latitude < -90 ||
        latitude > 90 ||
        longitude < -180 ||
        longitude > 180) {
      return null;
    }
    return LatLng(latitude, longitude);
  }
}

class _OrderProgressBar extends StatelessWidget {
  final String status;
  const _OrderProgressBar({required this.status});

  int get _currentStep {
    switch (status) {
      case 'ASSIGNED': return 0;
      case 'PICKING_UP': return 1;
      case 'DELIVERING': return 2;
      case 'COMPLETED': return 3;
      default:
        if (status == 'CANCELLED') return -2;
        return -1;
    }
  }

  @override
  Widget build(BuildContext context) {
    final step = _currentStep;
    if (step < 0) {
      return Text(
        OrderStatusHelper.label(status),
        style: const TextStyle(fontSize: 20, fontWeight: FontWeight.bold, color: AppTheme.primaryColor),
      );
    }

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _buildStep(0, 'รับออเดอร์', step),
          _buildLine(0, step),
          _buildStep(1, 'กำลังไปรับ', step),
          _buildLine(1, step),
          _buildStep(2, 'กำลังนำส่ง', step),
          _buildLine(2, step),
          _buildStep(3, 'ส่งสำเร็จ', step),
        ],
      ),
    );
  }

  Widget _buildStep(int stepIndex, String label, int currentStep) {
    final isActive = currentStep >= stepIndex;
    return Column(
      children: [
        Container(
          width: 24,
          height: 24,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            color: isActive ? AppTheme.primaryColor : Colors.grey[300],
          ),
          child: isActive ? const Icon(Icons.check, size: 16, color: Colors.white) : null,
        ),
        const SizedBox(height: 6),
        SizedBox(
          width: 50,
          child: Text(
            label,
            textAlign: TextAlign.center,
            style: TextStyle(
              fontSize: 10,
              color: isActive ? Colors.black87 : Colors.grey,
              fontWeight: isActive ? FontWeight.bold : FontWeight.normal,
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildLine(int stepIndex, int currentStep) {
    final isActive = currentStep > stepIndex;
    return Expanded(
      child: Container(
        height: 2,
        color: isActive ? AppTheme.primaryColor : Colors.grey[300],
        margin: const EdgeInsets.only(top: 12),
      ),
    );
  }
}

String _formatDistance(double? meters) {
  if (meters == null) return '--';
  if (meters < 1000) {
    return '${meters.toStringAsFixed(0)} m';
  } else {
    return '${(meters / 1000).toStringAsFixed(1)} km';
  }
}

String _formatDuration(double? seconds) {
  if (seconds == null) return '--';
  final minutes = (seconds / 60).round();
  if (minutes < 60) {
    return '$minutes mins';
  } else {
    final hours = minutes ~/ 60;
    final remainingMins = minutes % 60;
    if (remainingMins == 0) {
      return '$hours hrs';
    } else {
      return '$hours hrs $remainingMins mins';
    }
  }
}

