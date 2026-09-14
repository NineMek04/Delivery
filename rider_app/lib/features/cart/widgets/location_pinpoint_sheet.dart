import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:geolocator/geolocator.dart';
import 'package:latlong2/latlong.dart';
import '../../../app/app_theme.dart';

class LocationPinpointSheet extends StatefulWidget {
  final LatLng initialLocation;

  const LocationPinpointSheet({
    super.key,
    required this.initialLocation,
  });

  static Future<LatLng?> show(
    BuildContext context, {
    required LatLng initialLocation,
  }) {
    return showModalBottomSheet<LatLng>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (context) => LocationPinpointSheet(initialLocation: initialLocation),
    );
  }

  @override
  State<LocationPinpointSheet> createState() => _LocationPinpointSheetState();
}

class _LocationPinpointSheetState extends State<LocationPinpointSheet> {
  late LatLng _selectedLocation;
  late final MapController _mapController;
  bool _isLoadingGps = false;

  @override
  void initState() {
    super.initState();
    _selectedLocation = widget.initialLocation;
    _mapController = MapController();
  }

  @override
  void dispose() {
    _mapController.dispose();
    super.dispose();
  }

  Future<void> _getCurrentGps() async {
    setState(() => _isLoadingGps = true);
    try {
      final position = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(accuracy: LocationAccuracy.high),
      );
      final newLoc = LatLng(position.latitude, position.longitude);
      setState(() {
        _selectedLocation = newLoc;
      });
      _mapController.move(newLoc, 16.0);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('ไม่สามารถดึงตำแหน่งปัจจุบันได้: $e'),
            backgroundColor: AppTheme.errorColor,
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _isLoadingGps = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Container(
      height: MediaQuery.of(context).size.height * 0.82,
      decoration: BoxDecoration(
        color: isDark ? const Color(0xFF1E1E2E) : Colors.white,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(24)),
      ),
      child: Column(
        children: [
          // Drag handle & Header
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
            child: Column(
              children: [
                Container(
                  width: 40,
                  height: 4,
                  decoration: BoxDecoration(
                    color: Colors.grey[300],
                    borderRadius: BorderRadius.circular(2),
                  ),
                ),
                const SizedBox(height: 12),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    const Row(
                      children: [
                        Icon(Icons.pin_drop_rounded, color: AppTheme.primaryColor, size: 24),
                        SizedBox(width: 8),
                        Text(
                          'เลือกจุดจัดส่งบนแผนที่',
                          style: TextStyle(
                            fontSize: 18,
                            fontWeight: FontWeight.bold,
                          ),
                        ),
                      ],
                    ),
                    IconButton(
                      icon: const Icon(Icons.close),
                      onPressed: () => Navigator.pop(context),
                    ),
                  ],
                ),
                Text(
                  'แตะหรือลากแผนที่เพื่อเลือกตำแหน่งจัดส่งที่แม่นยำ เช่น หน้าประตู หรือป้อมยาม',
                  style: TextStyle(fontSize: 12, color: Colors.grey[600]),
                ),
              ],
            ),
          ),

          // Map Area with center target
          Expanded(
            child: Stack(
              children: [
                FlutterMap(
                  mapController: _mapController,
                  options: MapOptions(
                    initialCenter: _selectedLocation,
                    initialZoom: 16.0,
                    onTap: (tapPosition, point) {
                      setState(() {
                        _selectedLocation = point;
                      });
                    },
                    onPositionChanged: (camera, hasGesture) {
                      if (hasGesture) {
                        setState(() {
                          _selectedLocation = camera.center;
                        });
                      }
                    },
                  ),
                  children: [
                    TileLayer(
                      urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                      userAgentPackageName: 'com.delivery.app',
                    ),
                    MarkerLayer(
                      markers: [
                        Marker(
                          point: _selectedLocation,
                          width: 50,
                          height: 50,
                          child: const Icon(
                            Icons.location_on,
                            size: 46,
                            color: Colors.redAccent,
                          ),
                        ),
                      ],
                    ),
                  ],
                ),

                // Center floating crosshair indicator
                const Center(
                  child: Padding(
                    padding: EdgeInsets.only(bottom: 24),
                    child: Icon(
                      Icons.accessibility_new,
                      size: 0,
                    ),
                  ),
                ),

                // My GPS button
                Positioned(
                  right: 16,
                  bottom: 16,
                  child: FloatingActionButton.small(
                    heroTag: 'pinpoint_my_location_btn',
                    backgroundColor: Colors.white,
                    foregroundColor: AppTheme.primaryColor,
                    onPressed: _isLoadingGps ? null : _getCurrentGps,
                    child: _isLoadingGps
                        ? const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.my_location),
                  ),
                ),
              ],
            ),
          ),

          // Bottom Confirmation Bar
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 16),
            decoration: BoxDecoration(
              color: isDark ? const Color(0xFF27273A) : Colors.white,
              boxShadow: [
                BoxShadow(
                  color: Colors.black.withOpacity(0.08),
                  blurRadius: 10,
                  offset: const Offset(0, -3),
                ),
              ],
            ),
            child: SafeArea(
              top: false,
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Row(
                    children: [
                      const Icon(Icons.location_searching, size: 18, color: Colors.grey),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          'พิกัด: ${_selectedLocation.latitude.toStringAsFixed(6)}, ${_selectedLocation.longitude.toStringAsFixed(6)}',
                          style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w500),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 12),
                  SizedBox(
                    width: double.infinity,
                    child: ElevatedButton.icon(
                      style: ElevatedButton.styleFrom(
                        backgroundColor: AppTheme.primaryColor,
                        padding: const EdgeInsets.symmetric(vertical: 14),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                        elevation: 2,
                      ),
                      icon: const Icon(Icons.check_circle_outline, color: Colors.white),
                      label: const Text(
                        'ยืนยันพิกัดจัดส่งนี้',
                        style: TextStyle(fontSize: 15, fontWeight: FontWeight.bold, color: Colors.white),
                      ),
                      onPressed: () {
                        Navigator.pop(context, _selectedLocation);
                      },
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
