import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../models/order.dart';
import '../utils/order_status_helper.dart';
import 'status_badge.dart';

/// การ์ดแสดงรายละเอียดออเดอร์ — ปรับปรุงใหม่ให้รองรับข้อมูลเชิงลึก (Active Delivery).
class OrderCard extends StatelessWidget {
  final OrderDto order;
  final VoidCallback? onPrimaryAction;
  final String? primaryActionLabel;
  final bool isLoading;
  final bool showItems;

  const OrderCard({
    super.key,
    required this.order,
    this.onPrimaryAction,
    this.primaryActionLabel,
    this.isLoading = false,
    this.showItems = true,
  });

  Future<void> _makeCall(String? number) async {
    if (number == null || number.isEmpty) return;
    final url = Uri.parse('tel:$number');
    if (await canLaunchUrl(url)) {
      await launchUrl(url);
    }
  }

  @override
  Widget build(BuildContext context) {
    final fee = NumberFormat.currency(locale: 'th', symbol: '฿', decimalDigits: 0)
        .format(order.deliveryFee);
    
    final timeFormat = DateFormat('HH:mm');
    final expectedTime = order.expectedDeliveryTime != null 
        ? timeFormat.format(order.expectedDeliveryTime!) 
        : '—';

    return Card(
      margin: const EdgeInsets.only(bottom: 16),
      elevation: 2,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Header: Tracking Code & Status
            Row(
              children: [
                Container(
                  padding: const EdgeInsets.all(8),
                  decoration: BoxDecoration(
                    color: Theme.of(context).colorScheme.primary.withOpacity(0.1),
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: Icon(Icons.receipt_long, color: Theme.of(context).colorScheme.primary, size: 20),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        order.trackingCode ?? order.id.substring(0, 8),
                        style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.bold),
                      ),
                      Text(
                        'สร้างเมื่อ: ${order.createdAt != null ? DateFormat('dd/MM HH:mm').format(order.createdAt!) : '—'}',
                        style: Theme.of(context).textTheme.bodySmall?.copyWith(color: Colors.grey),
                      ),
                    ],
                  ),
                ),
                StatusBadge(status: order.status),
              ],
            ),
            
            const Divider(height: 24),
            
            // Expected Time & Fee Row
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                _infoChip(context, Icons.access_time, 'ส่งภายใน: $expectedTime', Colors.orange),
                _infoChip(context, Icons.payments_outlined, 'ค่าส่ง: $fee', Colors.green),
                _infoChip(context, Icons.route, '${order.distanceKm.toStringAsFixed(1)} km', Colors.blue),
              ],
            ),
            
            const SizedBox(height: 16),
            
            // Locations
            _locationRow(
              context,
              Icons.store,
              'จุดรับ (ร้านค้า)',
              order.pickupLat,
              order.pickupLng,
              isPickup: true,
            ),
            const Padding(
              padding: EdgeInsets.only(left: 17),
              child: SizedBox(height: 12, child: VerticalDivider(width: 1, thickness: 1, color: Colors.grey)),
            ),
            _locationRow(
              context,
              Icons.location_on,
              'จุดส่ง (ลูกค้า)',
              order.dropoffLat,
              order.dropoffLng,
              isPickup: false,
            ),
            
            // Items List (if expanded or active)
            if (showItems && order.items.isNotEmpty) ...[
              const Divider(height: 32),
              Text(
                'รายการสินค้า (${order.items.length})',
                style: Theme.of(context).textTheme.titleSmall?.copyWith(fontWeight: FontWeight.bold),
              ),
              const SizedBox(height: 8),
              ...order.items.map((item) => Padding(
                padding: const EdgeInsets.only(bottom: 4),
                child: Row(
                  children: [
                    Text('${item.quantity}x', style: const TextStyle(fontWeight: FontWeight.bold, color: Colors.blue)),
                    const SizedBox(width: 8),
                    Expanded(child: Text(item.name)),
                    Text(NumberFormat.currency(locale: 'th', symbol: '฿', decimalDigits: 0).format(item.totalPrice)),
                  ],
                ),
              )),
            ],

            // Action Buttons
            if (onPrimaryAction != null && primaryActionLabel != null) ...[
              const SizedBox(height: 20),
              Row(
                children: [
                  // Contact Buttons (Placeholders - would need phone numbers in DTO)
                  _contactButton(context, Icons.phone_in_talk, 'ร้าน', () => _makeCall(null)),
                  const SizedBox(width: 8),
                  _contactButton(context, Icons.person_outline, 'ลูกค้า', () => _makeCall(null)),
                  const SizedBox(width: 12),
                  Expanded(
                    child: ElevatedButton(
                      onPressed: isLoading ? null : onPrimaryAction,
                      style: ElevatedButton.styleFrom(
                        padding: const EdgeInsets.symmetric(vertical: 12),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
                      ),
                      child: isLoading
                          ? const SizedBox(
                              height: 20,
                              width: 20,
                              child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                            )
                          : Text(primaryActionLabel!, style: const TextStyle(fontWeight: FontWeight.bold)),
                    ),
                  ),
                ],
              ),
            ],
          ],
        ),
      ),
    );
  }

  Widget _infoChip(BuildContext context, IconData icon, String label, Color color) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: color.withOpacity(0.1),
        borderRadius: BorderRadius.circular(6),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 14, color: color),
          const SizedBox(width: 4),
          Text(label, style: TextStyle(fontSize: 11, color: color, fontWeight: FontWeight.bold)),
        ],
      ),
    );
  }

  Widget _contactButton(BuildContext context, IconData icon, String label, VoidCallback onTap) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(10),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
        decoration: BoxDecoration(
          border: Border.all(color: Colors.grey.shade300),
          borderRadius: BorderRadius.circular(10),
        ),
        child: Column(
          children: [
            Icon(icon, size: 18, color: Colors.grey.shade700),
            Text(label, style: const TextStyle(fontSize: 10)),
          ],
        ),
      ),
    );
  }

  Widget _locationRow(
    BuildContext context,
    IconData icon,
    String label,
    double? lat,
    double? lng, {
    required bool isPickup,
  }) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(
          icon,
          size: 20,
          color: isPickup ? Colors.orange : Colors.green,
        ),
        const SizedBox(width: 12),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                label,
                style: const TextStyle(fontSize: 12, fontWeight: FontWeight.bold),
              ),
              Text(
                lat != null && lng != null ? '${lat.toStringAsFixed(5)}, ${lng.toStringAsFixed(5)}' : 'ไม่ระบุพิกัด',
                style: Theme.of(context).textTheme.bodySmall?.copyWith(color: Colors.grey.shade600),
              ),
            ],
          ),
        ),
      ],
    );
  }
}
