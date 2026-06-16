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

class _CustomerTrackingScreenState extends ConsumerState<CustomerTrackingScreen> {
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

  @override
  void initState() {
    super.initState();
    Future.microtask(() => ref.read(activeOrderProvider.notifier).watchOrder(widget.orderId));
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
      });
      Future.microtask(
        () => ref
            .read(activeOrderProvider.notifier)
            .watchOrder(widget.orderId),
      );
    }
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
      
      final now = DateTime.now();
      if (_lastUpdateReceivedTime != null) {
        final diff = now.difference(_lastUpdateReceivedTime!);
        var seconds = diff.inSeconds;
        if (seconds < 1) seconds = 1;
        if (seconds > 6) seconds = 6;
        _animationDuration = Duration(seconds: seconds);
      }
      _lastUpdateReceivedTime = now;
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
                              final riderLatLng = state.riderLat != null && state.riderLng != null
                                  ? LatLng(state.riderLat!, state.riderLng!)
                                  : (pickupPoint ?? dropoffPoint ?? const LatLng(17.4138, 102.7872));

                              return TweenAnimationBuilder<LatLng>(
                                tween: LatLngTween(
                                  begin: _lastAnimatedRiderPoint ?? riderLatLng,
                                  end: riderLatLng,
                                ),
                                duration: _animationDuration,
                                builder: (context, animatedRiderPoint, child) {
                                  _lastAnimatedRiderPoint = animatedRiderPoint;

                                  return FlutterMap(
                                    mapController: _mapController,
                                    options: MapOptions(
                                      initialCenter: pickupPoint ??
                                          dropoffPoint ??
                                          const LatLng(17.4138, 102.7872),
                                      initialZoom: 14,
                                    ),
                                    children: [
                                      TileLayer(
                                        urlTemplate: kIsWeb
                                            ? '/map-tiles/{z}/{x}/{y}.png'
                                            : 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                                        userAgentPackageName: 'com.delivery.customer_app',
                                      ),
                                      if (_routePoints.isNotEmpty && state.riderLat != null && state.riderLng != null)
                                        PolylineLayer(
                                          polylines: [
                                            Polyline(
                                              points: _getTailRoute(_routePoints, animatedRiderPoint),
                                              color: Colors.blueAccent,
                                              strokeWidth: 4.5,
                                            ),
                                          ],
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
                                              point: animatedRiderPoint,
                                              width: 40,
                                              height: 40,
                                              child: const Icon(Icons.delivery_dining, color: AppTheme.primaryColor, size: 35),
                                            ),
                                        ],
                                      ),
                                    ],
                                  );
                                },
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

