import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../../models/order.dart';
import '../utils/order_status_helper.dart';
import 'status_badge.dart';

/// การ์ดแสดงรายละเอียดออเดอร์.
class OrderCard extends StatelessWidget {
  final OrderDto order;
  final VoidCallback? onPrimaryAction;
  final String? primaryActionLabel;
  final bool isLoading;

  const OrderCard({
    super.key,
    required this.order,
    this.onPrimaryAction,
    this.primaryActionLabel,
    this.isLoading = false,
  });

  @override
  Widget build(BuildContext context) {
    final fee = NumberFormat.currency(locale: 'th', symbol: '฿', decimalDigits: 0)
        .format(order.deliveryFee);

    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    order.trackingCode ?? order.id.substring(0, 8),
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
                StatusBadge(status: order.status, compact: true),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              OrderStatusHelper.label(order.status),
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const SizedBox(height: 12),
            _locationRow(
              context,
              Icons.store,
              'รับ',
              order.pickupLat,
              order.pickupLng,
            ),
            const SizedBox(height: 6),
            _locationRow(
              context,
              Icons.home,
              'ส่ง',
              order.dropoffLat,
              order.dropoffLng,
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Icon(Icons.payments_outlined, size: 16, color: Theme.of(context).colorScheme.secondary),
                const SizedBox(width: 4),
                Text(fee),
                const Spacer(),
                Text(
                  '${order.distanceKm.toStringAsFixed(1)} km',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ],
            ),
            if (onPrimaryAction != null && primaryActionLabel != null) ...[
              const SizedBox(height: 16),
              SizedBox(
                width: double.infinity,
                child: ElevatedButton(
                  onPressed: isLoading ? null : onPrimaryAction,
                  child: isLoading
                      ? const SizedBox(
                          height: 20,
                          width: 20,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : Text(primaryActionLabel!),
                ),
              ),
            ],
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
    double? lng,
  ) {
    final coords = lat != null && lng != null
        ? '${lat.toStringAsFixed(4)}, ${lng.toStringAsFixed(4)}'
        : '—';
    return Row(
      children: [
        Icon(icon, size: 18, color: Theme.of(context).colorScheme.primary),
        const SizedBox(width: 8),
        Text('$label: ', style: const TextStyle(fontWeight: FontWeight.w600)),
        Expanded(child: Text(coords, style: Theme.of(context).textTheme.bodySmall)),
      ],
    );
  }
}
