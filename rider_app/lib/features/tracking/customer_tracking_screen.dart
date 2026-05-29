import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';
import 'package:latlong2/latlong.dart';
import '../../app/app_theme.dart';
import '../../shared/utils/order_status_helper.dart';
import 'providers/tracking_provider.dart';

class CustomerTrackingScreen extends ConsumerStatefulWidget {
  final String orderId;

  const CustomerTrackingScreen({super.key, required this.orderId});

  @override
  ConsumerState<CustomerTrackingScreen> createState() => _CustomerTrackingScreenState();
}

class _CustomerTrackingScreenState extends ConsumerState<CustomerTrackingScreen> {
  final MapController _mapController = MapController();

  @override
  void initState() {
    super.initState();
    Future.microtask(() => ref.read(activeOrderProvider.notifier).watchOrder(widget.orderId));
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(activeOrderProvider);

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
                          child: FlutterMap(
                            mapController: _mapController,
                            options: MapOptions(
                              initialCenter: LatLng(
                                state.order!.pickupLat ?? 17.4138,
                                state.order!.pickupLng ?? 102.7872,
                              ),
                              initialZoom: 14,
                            ),
                            children: [
                              TileLayer(
                                urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                                userAgentPackageName: 'com.delivery.customer_app',
                              ),
                              MarkerLayer(
                                markers: [
                                  // Store Marker
                                  Marker(
                                    point: LatLng(state.order!.pickupLat!, state.order!.pickupLng!),
                                    width: 40,
                                    height: 40,
                                    child: const Icon(Icons.store, color: Colors.red, size: 30),
                                  ),
                                  // Customer Marker
                                  Marker(
                                    point: LatLng(state.order!.dropoffLat!, state.order!.dropoffLng!),
                                    width: 40,
                                    height: 40,
                                    child: const Icon(Icons.home, color: Colors.blue, size: 30),
                                  ),
                                  // Rider Marker (if available)
                                  if (state.riderLat != null && state.riderLng != null)
                                    Marker(
                                      point: LatLng(state.riderLat!, state.riderLng!),
                                      width: 40,
                                      height: 40,
                                      child: const Icon(Icons.delivery_dining, color: AppTheme.primaryColor, size: 35),
                                    ),
                                ],
                              ),
                            ],
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
                                  Row(
                                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                                    children: [
                                      Text(
                                        OrderStatusHelper.label(state.order!.status),
                                        style: const TextStyle(fontSize: 20, fontWeight: FontWeight.bold, color: AppTheme.primaryColor),
                                      ),
                                      const Icon(Icons.info_outline, color: Colors.grey),
                                    ],
                                  ),
                                  const Divider(height: 32),
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
}
