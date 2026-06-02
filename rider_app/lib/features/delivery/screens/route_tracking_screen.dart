import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:sqflite/sqflite.dart';

import '../../../core/location/tile_cache_service.dart';

// Mock Provider for demonstration
final locationProvider = StateProvider<LatLng>((ref) => const LatLng(17.4138, 102.7872));

class RouteTrackingScreen extends ConsumerStatefulWidget {
  final String orderId;

  const RouteTrackingScreen({super.key, required this.orderId});

  @override
  ConsumerState<RouteTrackingScreen> createState() => _RouteTrackingScreenState();
}

class _RouteTrackingScreenState extends ConsumerState<RouteTrackingScreen> {
  final MapController _mapController = MapController();
  bool _isPickedUp = false;
  String? _dbDir;

  @override
  void initState() {
    super.initState();
    _loadDbDir();
  }

  Future<void> _loadDbDir() async {
    try {
      final dbPath = await getDatabasesPath();
      if (mounted) {
        setState(() {
          _dbDir = dbPath;
        });
      }
    } catch (_) {}
  }

  // Mock Route Data
  final LatLng _pickupLocation = const LatLng(17.4200, 102.7900);
  final LatLng _dropoffLocation = const LatLng(17.4000, 102.7800);
  final List<LatLng> _routePoints = [
    const LatLng(17.4138, 102.7872),
    const LatLng(17.4150, 102.7880),
    const LatLng(17.4200, 102.7900),
  ];

  void _callCustomer() async {
    final Uri launchUri = Uri(scheme: 'tel', path: '0812345678');
    if (await canLaunchUrl(launchUri)) {
      await launchUrl(launchUri);
    } else {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Could not launch phone app.')),
        );
      }
    }
  }

  void _markStatus() {
    if (!_isPickedUp) {
      setState(() {
        _isPickedUp = true;
      });
      // In a real app, we'd fetch a new route to the dropoff here
    } else {
      // Proceed to Delivery Confirmation
      // context.goNamed('delivery_confirmation', pathParameters: {'id': widget.orderId});
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Navigating to Delivery Confirmation...')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final currentLocation = ref.watch(locationProvider);

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
          // Flutter Map
          FlutterMap(
            mapController: _mapController,
            options: MapOptions(
              initialCenter: currentLocation,
              initialZoom: 14.0,
            ),
            children: [
              TileLayer(
                urlTemplate: 'https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png',
                subdomains: const ['a', 'b', 'c', 'd'],
                tileProvider: _dbDir != null
                    ? CachedTileProvider(dbDir: _dbDir!)
                    : NetworkTileProvider(),
              ),
              PolylineLayer(
                polylines: [
                  Polyline(
                    points: _routePoints,
                    strokeWidth: 4.0,
                    color: Colors.blueAccent,
                  ),
                ],
              ),
              MarkerLayer(
                markers: [
                  // Rider Marker
                  Marker(
                    point: currentLocation,
                    width: 40,
                    height: 40,
                    child: const Icon(Icons.motorcycle, color: Colors.blue, size: 30),
                  ),
                  // Pickup Marker
                  if (!_isPickedUp)
                    Marker(
                      point: _pickupLocation,
                      width: 40,
                      height: 40,
                      child: Container(
                        decoration: const BoxDecoration(color: Colors.orange, shape: BoxShape.circle),
                        child: const Icon(Icons.store, color: Colors.white, size: 20),
                      ),
                    ),
                  // Dropoff Marker
                  Marker(
                    point: _dropoffLocation,
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
          ),

          // Bottom Sheet Overlay
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
                  // ETA and Distance
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
                            '12 mins',
                            style: GoogleFonts.poppins(fontSize: 24, fontWeight: FontWeight.bold, color: Colors.black87),
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
                            '3.2 km',
                            style: GoogleFonts.poppins(fontSize: 20, fontWeight: FontWeight.w600, color: Colors.black87),
                          ),
                        ],
                      ),
                    ],
                  ),
                  const SizedBox(height: 24),
                  
                  // Action Button
                  SizedBox(
                    width: double.infinity,
                    child: ElevatedButton(
                      onPressed: _markStatus,
                      style: ElevatedButton.styleFrom(
                        backgroundColor: _isPickedUp ? Colors.green : Colors.orange,
                        padding: const EdgeInsets.symmetric(vertical: 16),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                        elevation: 0,
                      ),
                      child: Text(
                        _isPickedUp ? 'MARK DELIVERED' : 'MARK PICKED UP',
                        style: GoogleFonts.poppins(fontSize: 16, fontWeight: FontWeight.bold, color: Colors.white),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
          
          // Re-center button
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
