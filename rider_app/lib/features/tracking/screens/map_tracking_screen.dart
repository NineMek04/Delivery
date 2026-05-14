import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

/// Map Tracking Screen — แผนที่ real-time + เส้นทางส่งของ.
///
/// ใช้ `flutter_map` (OpenStreetMap — ฟรี) เป็นตัวหลัก.
/// เตรียม `google_maps_flutter` ไว้เผื่อเปลี่ยนในอนาคต (ต้องมี API Key).
///
/// แสดง:
/// - ตำแหน่งปัจจุบันของ Rider (GPS)
/// - จุดรับ/ส่งสินค้า (pickup/dropoff markers)
/// - เส้นทางที่ AI คำนวณ (VRP waypoint sequence)
///
/// Data Flow:
/// ```
/// GPS (Geolocator) → LocationService → SignalR → Backend → PostGIS
///                                                    ↓
///                                              AI Service (OR-Tools)
///                                                    ↓
///                                              Route Result → Map
/// ```
///
/// TODO: ใส่ logic จริง — GPS tracking, route overlay, markers
class MapTrackingScreen extends ConsumerWidget {
  const MapTrackingScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('แผนที่'),
        actions: [
          IconButton(
            icon: const Icon(Icons.my_location),
            onPressed: () {
              // TODO: Center map on current location
            },
          ),
        ],
      ),
      body: Stack(
        children: [
          // ── OpenStreetMap (flutter_map) ─────────────────────────────
          FlutterMap(
            options: MapOptions(
              // ค่าเริ่มต้น: ศูนย์กลางประเทศไทย
              initialCenter: const LatLng(13.7563, 100.5018),
              initialZoom: 13.0,
            ),
            children: [
              // Tile Layer — OpenStreetMap (ฟรี)
              TileLayer(
                urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                userAgentPackageName: 'com.delivery.rider_app',
              ),

              // TODO: เพิ่ม MarkerLayer สำหรับ:
              // - ตำแหน่ง Rider ปัจจุบัน
              // - Pickup/Dropoff markers
              // - Route polyline

              // Placeholder marker
              const MarkerLayer(
                markers: [
                  Marker(
                    point: LatLng(13.7563, 100.5018),
                    width: 40,
                    height: 40,
                    child: Icon(
                      Icons.location_on,
                      color: Colors.blue,
                      size: 40,
                    ),
                  ),
                ],
              ),
            ],
          ),

          // ── Bottom Info Card ────────────────────────────────────────
          Positioned(
            left: 16,
            right: 16,
            bottom: 16,
            child: Card(
              child: Padding(
                padding: const EdgeInsets.all(16.0),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      'GPS Tracking',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    const SizedBox(height: 8),
                    Text(
                      '[ Map Tracking Placeholder — ใช้ OpenStreetMap (ฟรี) ]',
                      style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                        color: Theme.of(context).colorScheme.primary,
                      ),
                    ),
                    const SizedBox(height: 12),
                    SizedBox(
                      width: double.infinity,
                      child: ElevatedButton.icon(
                        onPressed: () {
                          // TODO: Start/Stop GPS tracking
                        },
                        icon: const Icon(Icons.gps_fixed),
                        label: const Text('เริ่มติดตาม GPS'),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
