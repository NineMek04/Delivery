import 'dart:async';
import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:sqflite/sqflite.dart';

import '../../../core/location/tile_cache_service.dart';
import '../../../core/location/location_service.dart';
import '../../../core/api/services/rider_route_api_service.dart';
import '../../../models/order.dart';
import '../providers/delivery_provider.dart';
import '../../tracking/providers/tracking_provider.dart';
import '../../tracking/services/simulated_journey_service.dart';

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

final locationStateProvider = StateProvider<LatLng>((ref) => const LatLng(17.4138, 102.7872));

class RouteTrackingScreen extends ConsumerStatefulWidget {
  final String orderId;

  const RouteTrackingScreen({super.key, required this.orderId});

  @override
  ConsumerState<RouteTrackingScreen> createState() => _RouteTrackingScreenState();
}

class _RouteTrackingScreenState extends ConsumerState<RouteTrackingScreen> {
  final MapController _mapController = MapController();
  bool _isPickedUp = false;
  bool _isTrackingStarted = false;
  bool _isUpdatingStatus = false;
  double _currentDistance = double.infinity;
  String? _dbDir;
  List<LatLng> _currentRoutePoints = [];
  LatLng? _lastAnimatedLocation;

  @override
  void initState() {
    super.initState();
    _loadDbDir();
    final simService = ref.read(simulatedJourneyProvider);
    if (!simService.isRunning) {
      final tracking = ref.read(locationServiceProvider);
      if (tracking.latitude != null && tracking.longitude != null) {
        ref.read(locationStateProvider.notifier).state = LatLng(tracking.latitude!, tracking.longitude!);
      }
    }
    Future.microtask(() => ref.read(activeOrderProvider.notifier).watchOrder(widget.orderId));
  }

  Future<void> _loadDbDir() async {
    try {
      final dbPath = await getDatabasesPath();
      if (mounted) setState(() => _dbDir = dbPath);
    } catch (_) {}
  }

  @override
  void dispose() {
    final simService = ref.read(simulatedJourneyProvider);
    simService.onDistanceUpdated = null;
    simService.onDestinationReached = null;
    _mapController.dispose();
    super.dispose();
  }

  void _callCustomer() async {
    final Uri launchUri = Uri(scheme: 'tel', path: '0812345678');
    if (await canLaunchUrl(launchUri)) {
      await launchUrl(launchUri);
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

  Future<void> _startGpsTracking(ActiveOrderState state) async {
    if (state.order == null) return;
    final order = state.order!;
    final status = order.status.toUpperCase();

    if (status == 'ASSIGNED') {
      final updated = await _updateOrderStatus(order.id, 'PICKING_UP');
      if (!updated || !mounted) return;
    } else if (status == 'DELIVERING') {
      if (order.dropoffLat == null || order.dropoffLng == null) return;
      setState(() {
        _isTrackingStarted = true;
        _isPickedUp = true;
      });
      await _startDeliveryJourney(order);
      return;
    } else if (status != 'PICKING_UP') {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Order cannot be tracked in state $status')),
      );
      return;
    }

    if (order.pickupLat == null || order.pickupLng == null) return;
    setState(() => _isTrackingStarted = true);

    final simService = ref.read(simulatedJourneyProvider);
    
    if (simService.isRunning) {
      setState(() {
        _currentRoutePoints = simService.currentRoute;
      });
      simService.onDistanceUpdated = (dist) {
        if (mounted) setState(() => _currentDistance = dist);
      };
      simService.onDestinationReached = () {
        if (mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('Reached Pickup Location! Slide to Pick Up.')),
          );
        }
      };
      return;
    }

    // Resolve OSRM route to pickup dynamically
    List<LatLng> pickupRoute = [];
    final currentPos = ref.read(locationStateProvider);
    final pickup = LatLng(order.pickupLat!, order.pickupLng!);
    try {
      final route = await ref.read(riderRouteApiServiceProvider).resolve(
        orderId: order.id,
        routePhase: 'PICKUP',
        currentLat: currentPos.latitude,
        currentLng: currentPos.longitude,
      );
      pickupRoute = simService.decodePolyline(route.encodedPolyline);
    } catch (e) {
      pickupRoute = [currentPos, pickup];
    }

    setState(() {
      _currentRoutePoints = pickupRoute;
    });

    simService.onDistanceUpdated = (dist) {
      if (mounted) setState(() => _currentDistance = dist);
    };

    simService.onDestinationReached = () {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Reached Pickup Location! Slide to Pick Up.')),
        );
      }
    };

    simService.startJourney(
      routeCoords: pickupRoute, 
      destination: pickup, 
      locationStateController: ref.read(locationStateProvider.notifier),
    );
  }

  Future<void> _markStatus(ActiveOrderState state) async {
    if (state.order == null) return;
    final order = state.order!;
    final simService = ref.read(simulatedJourneyProvider);

    if (!_isPickedUp) {
      if (order.dropoffLat == null || order.dropoffLng == null) return;
      final updated = await _updateOrderStatus(order.id, 'DELIVERING');
      if (!updated || !mounted) return;
      setState(() {
        _isPickedUp = true;
        _currentDistance = double.infinity;
      });

      await _startDeliveryJourney(order);

    } else {
      final updated = await _updateOrderStatus(order.id, 'COMPLETED');
      if (!updated || !mounted) return;
      simService.stopJourney();
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Delivery Completed!')),
      );
      if (mounted) {
        context.pop();
      }
    }
  }

  Future<void> _startDeliveryJourney(OrderDto order) async {
    final simService = ref.read(simulatedJourneyProvider);
    List<LatLng> deliveryRoute = [];
    final currentPos = ref.read(locationStateProvider);
    final dropoff = LatLng(order.dropoffLat!, order.dropoffLng!);

    if (order.encodedPolyline != null) {
      deliveryRoute = simService.decodePolyline(order.encodedPolyline!);
    } else {
      try {
        final route = await ref.read(riderRouteApiServiceProvider).resolve(
          orderId: order.id,
          routePhase: 'DELIVERY',
          currentLat: currentPos.latitude,
          currentLng: currentPos.longitude,
        );
        deliveryRoute = simService.decodePolyline(route.encodedPolyline);
      } catch (_) {
        deliveryRoute = [currentPos, dropoff];
      }
    }

    setState(() {
      _currentRoutePoints = deliveryRoute;
    });

    simService.onDistanceUpdated = (dist) {
      if (mounted) setState(() => _currentDistance = dist);
    };

    simService.onDestinationReached = () {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Reached Dropoff Location! Slide to Complete.')),
        );
      }
    };

    simService.startJourney(
      routeCoords: deliveryRoute,
      destination: dropoff,
      locationStateController: ref.read(locationStateProvider.notifier),
    );
  }

  Future<bool> _updateOrderStatus(String orderId, String status) async {
    if (_isUpdatingStatus) return false;
    setState(() => _isUpdatingStatus = true);
    await ref
        .read(deliveryNotifierProvider.notifier)
        .updateOrderStatus(orderId, status);
    if (!mounted) return false;

    final error = ref.read(deliveryNotifierProvider).error;
    setState(() => _isUpdatingStatus = false);
    if (error == null || error.startsWith('Offline:')) return true;

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(error)),
    );
    return false;
  }

  @override
  Widget build(BuildContext context) {
    final currentLocation = ref.watch(locationStateProvider);
    final state = ref.watch(activeOrderProvider);
    
    if (state.isLoading) return const Scaffold(body: Center(child: CircularProgressIndicator()));
    if (state.order == null) return const Scaffold(body: Center(child: Text('Order not found')));

    final order = state.order!;
    final pickupLocation = LatLng(order.pickupLat ?? 17.4138, order.pickupLng ?? 102.7872);
    final dropoffLocation = LatLng(order.dropoffLat ?? 17.4000, order.dropoffLng ?? 102.7800);
    
    final bool canSlide = _isTrackingStarted && _currentDistance <= 50;

    return Scaffold(
      appBar: AppBar(
        title: Text(
          'Tracking Order #${widget.orderId.substring(0, 8)}',
          style: GoogleFonts.poppins(fontWeight: FontWeight.bold, fontSize: 16),
        ),
        backgroundColor: Colors.white,
        foregroundColor: Colors.black87,
        elevation: 0,
        actions: [
          IconButton(
            icon: const Icon(Icons.phone, color: Colors.blue),
            onPressed: _callCustomer,
          ),
        ],
      ),
      body: Stack(
        children: [
          TweenAnimationBuilder<LatLng>(
            tween: LatLngTween(
              begin: _lastAnimatedLocation ?? currentLocation,
              end: currentLocation,
            ),
            duration: const Duration(seconds: 1),
            builder: (context, animatedLocation, child) {
              _lastAnimatedLocation = animatedLocation;

              return FlutterMap(
                mapController: _mapController,
                options: MapOptions(
                  initialCenter: currentLocation,
                  initialZoom: 14.0,
                ),
                children: [
                  TileLayer(
                    urlTemplate: kIsWeb
                        ? '/map-tiles/{z}/{x}/{y}.png'
                        : 'https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png',
                    subdomains: kIsWeb ? const [] : const ['a', 'b', 'c', 'd'],
                    tileProvider: !kIsWeb && _dbDir != null
                        ? CachedTileProvider(dbDir: _dbDir!)
                        : NetworkTileProvider(),
                  ),
                  if (_currentRoutePoints.isNotEmpty)
                    PolylineLayer(
                      polylines: [
                        Polyline(
                          points: _getTailRoute(_currentRoutePoints, animatedLocation),
                          color: Colors.blueAccent,
                          strokeWidth: 4.5,
                        ),
                      ],
                    ),
                  MarkerLayer(
                    markers: [
                      Marker(
                        point: animatedLocation,
                        width: 40,
                        height: 40,
                        child: const Icon(Icons.motorcycle, color: Colors.blue, size: 30),
                      ),
                      if (!_isPickedUp)
                        Marker(
                          point: pickupLocation,
                          width: 40,
                          height: 40,
                          child: Container(
                            decoration: const BoxDecoration(color: Colors.orange, shape: BoxShape.circle),
                            child: const Icon(Icons.store, color: Colors.white, size: 20),
                          ),
                        ),
                      Marker(
                        point: dropoffLocation,
                        width: 40,
                        height: 40,
                        child: Container(
                          decoration: const BoxDecoration(color: Colors.green, shape: BoxShape.circle),
                          child: const Icon(Icons.home, color: Colors.white, size: 20),
                        ),
                      ),
                    ],
                  ),
                ],
              );
            },
          ),

          Positioned(
            left: 0,
            right: 0,
            bottom: 0,
            child: Container(
              padding: const EdgeInsets.all(24),
              decoration: const BoxDecoration(
                color: Colors.white,
                borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
                boxShadow: [
                  BoxShadow(color: Colors.black12, blurRadius: 10, offset: Offset(0, -2))
                ],
              ),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            _isPickedUp ? 'TO DROPOFF' : 'TO PICKUP',
                            style: GoogleFonts.poppins(color: Colors.grey, fontSize: 12, fontWeight: FontWeight.bold),
                          ),
                          Text(
                            'Status: ${order.status}',
                            style: GoogleFonts.poppins(fontSize: 16, fontWeight: FontWeight.bold, color: Colors.black87),
                          ),
                        ],
                      ),
                      Column(
                        crossAxisAlignment: CrossAxisAlignment.end,
                        children: [
                          Text(
                            'DISTANCE',
                            style: GoogleFonts.poppins(color: Colors.grey, fontSize: 12, fontWeight: FontWeight.bold),
                          ),
                          Text(
                            _currentDistance == double.infinity ? '--' : '${(_currentDistance).toStringAsFixed(0)} m',
                            style: GoogleFonts.poppins(fontSize: 20, fontWeight: FontWeight.w600, color: Colors.black87),
                          ),
                        ],
                      ),
                    ],
                  ),
                  const SizedBox(height: 24),
                  
                  if (!_isTrackingStarted)
                    SizedBox(
                      width: double.infinity,
                      child: ElevatedButton(
                        onPressed: _isUpdatingStatus
                            ? null
                            : () => _startGpsTracking(state),
                        style: ElevatedButton.styleFrom(
                          backgroundColor: Colors.blueAccent,
                          padding: const EdgeInsets.symmetric(vertical: 16),
                          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                        ),
                        child: Text(
                          'START GPS TRACKING',
                          style: GoogleFonts.poppins(fontSize: 16, fontWeight: FontWeight.bold, color: Colors.white),
                        ),
                      ),
                    )
                  else
                    SlideToConfirm(
                      text: _isPickedUp ? 'Slide to Complete' : 'Slide to Pick Up',
                      isEnabled: canSlide && !_isUpdatingStatus,
                      color: _isPickedUp ? Colors.green : Colors.orange,
                      onConfirmed: () {
                        _markStatus(state);
                      },
                    ),
                ],
              ),
            ),
          ),
          
          Positioned(
            right: 16,
            bottom: 180,
            child: FloatingActionButton(
              backgroundColor: Colors.white,
              onPressed: () {
                _mapController.move(currentLocation, 15.0);
              },
              child: const Icon(Icons.my_location, color: Colors.blue),
            ),
          ),
        ],
      ),
    );
  }
}

class SlideToConfirm extends StatefulWidget {
  final String text;
  final VoidCallback onConfirmed;
  final bool isEnabled;
  final Color color;

  const SlideToConfirm({
    Key? key,
    required this.text,
    required this.onConfirmed,
    this.isEnabled = true,
    this.color = Colors.orange,
  }) : super(key: key);

  @override
  State<SlideToConfirm> createState() => _SlideToConfirmState();
}

class _SlideToConfirmState extends State<SlideToConfirm> {
  double _position = 0;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final maxWidth = constraints.maxWidth;
        const sliderWidth = 60.0;
        final maxPosition = maxWidth - sliderWidth;

        return Container(
          height: 60,
          decoration: BoxDecoration(
            color: widget.isEnabled ? widget.color : Colors.grey[300],
            borderRadius: BorderRadius.circular(30),
          ),
          child: Stack(
            children: [
              Center(
                child: Text(
                  widget.isEnabled ? widget.text : 'Too far to confirm',
                  style: GoogleFonts.poppins(
                    fontSize: 16,
                    fontWeight: FontWeight.bold,
                    color: widget.isEnabled ? Colors.white : Colors.grey[500],
                  ),
                ),
              ),
              if (widget.isEnabled)
                Positioned(
                  left: _position,
                  child: GestureDetector(
                    onHorizontalDragUpdate: (details) {
                      setState(() {
                        _position += details.delta.dx;
                        if (_position < 0) _position = 0;
                        if (_position > maxPosition) _position = maxPosition;
                      });
                    },
                    onHorizontalDragEnd: (details) {
                      if (_position > maxPosition * 0.8) {
                        setState(() {
                          _position = maxPosition;
                        });
                        // Reset slider immediately after slight delay so it can be reused
                        Future.delayed(const Duration(milliseconds: 300), () {
                          if (mounted) setState(() => _position = 0);
                        });
                        widget.onConfirmed();
                      } else {
                        setState(() => _position = 0);
                      }
                    },
                    child: Container(
                      width: sliderWidth,
                      height: 60,
                      decoration: const BoxDecoration(
                        color: Colors.white,
                        shape: BoxShape.circle,
                        boxShadow: [
                          BoxShadow(color: Colors.black12, blurRadius: 4),
                        ],
                      ),
                      child: Icon(Icons.arrow_forward_ios, color: widget.color),
                    ),
                  ),
                ),
            ],
          ),
        );
      },
    );
  }
}
