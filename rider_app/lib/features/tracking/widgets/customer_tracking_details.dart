import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';
import '../../../app/app_theme.dart';
import '../../../models/order.dart';
import '../../../shared/widgets/order_review_dialog.dart';
import '../../delivery/screens/chat_screen.dart';
import '../providers/tracking_provider.dart';
import 'order_progress_bar.dart';

class CustomerTrackingDetailsSheet extends ConsumerWidget {
  final String orderId;
  final OrderDto order;
  final double? routeDuration;
  final double? routeDistance;

  const CustomerTrackingDetailsSheet({
    super.key,
    required this.orderId,
    required this.order,
    this.routeDuration,
    this.routeDistance,
  });

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Container(
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
            if (routeDuration != null || routeDistance != null) ...[
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
                        'ไรเดอร์กำลังนำส่ง! จะถึงในประมาณ ${formatDuration(routeDuration)} (${formatDistance(routeDistance)})',
                        style: const TextStyle(fontWeight: FontWeight.bold, color: Colors.blueAccent),
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 12),
            ],
            OrderProgressBar(status: order.status),
            if ((order.deliveryAddress != null && order.deliveryAddress!.isNotEmpty) ||
                (order.noteToRider != null && order.noteToRider!.isNotEmpty)) ...[
              const SizedBox(height: 10),
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(10),
                decoration: BoxDecoration(
                  color: Colors.amber.shade50,
                  borderRadius: BorderRadius.circular(10),
                  border: Border.all(color: Colors.amber.shade200),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (order.deliveryAddress != null && order.deliveryAddress!.isNotEmpty) ...[
                      Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          const Icon(Icons.location_on, size: 16, color: Colors.redAccent),
                          const SizedBox(width: 6),
                          Expanded(
                            child: Text(
                              'ที่อยู่จัดส่ง: ${order.deliveryAddress}',
                              style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
                            ),
                          ),
                        ],
                      ),
                    ],
                    if (order.noteToRider != null && order.noteToRider!.isNotEmpty) ...[
                      if (order.deliveryAddress != null && order.deliveryAddress!.isNotEmpty)
                        const SizedBox(height: 6),
                      Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          const Icon(Icons.delivery_dining, size: 16, color: Colors.blueAccent),
                          const SizedBox(width: 6),
                          Expanded(
                            child: Text(
                              'ข้อความถึงไรเดอร์: ${order.noteToRider}',
                              style: const TextStyle(fontSize: 12, color: Colors.black87),
                            ),
                          ),
                        ],
                      ),
                    ],
                  ],
                ),
              ),
            ],
            const Divider(height: 24),
            const Text(
              'รายการอาหาร',
              style: TextStyle(fontWeight: FontWeight.bold, fontSize: 16),
            ),
            const SizedBox(height: 12),
            ...order.items.map((item) => Padding(
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
                  NumberFormat.currency(locale: 'th', symbol: '฿', decimalDigits: 0).format(
                    order.deliveryFee + order.items.fold(0.0, (sum, item) => sum + item.totalPrice),
                  ),
                  style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18, color: AppTheme.primaryColor),
                ),
              ],
            ),
            const SizedBox(height: 16),
            SizedBox(
              width: double.infinity,
              child: OutlinedButton.icon(
                style: OutlinedButton.styleFrom(
                  padding: const EdgeInsets.symmetric(vertical: 12),
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                  side: const BorderSide(color: AppTheme.primaryColor),
                ),
                icon: const Icon(Icons.chat_bubble_outline, color: AppTheme.primaryColor, size: 20),
                label: Text(
                  order.status == 'COMPLETED' ? 'ดูประวัติการแชทกับไรเดอร์' : 'แชทกับไรเดอร์',
                  style: const TextStyle(color: AppTheme.primaryColor, fontWeight: FontWeight.bold),
                ),
                onPressed: () {
                  Navigator.of(context).push(
                    MaterialPageRoute(
                      builder: (context) => ChatScreen(
                        orderId: orderId,
                        initialStatus: order.status,
                      ),
                    ),
                  );
                },
              ),
            ),
            if (order.status == 'COMPLETED') ...[
              const SizedBox(height: 10),
              if (order.rating != null)
                Container(
                  width: double.infinity,
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: Colors.green.shade50,
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(color: Colors.green.shade200),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Row(
                        children: [
                          ...List.generate(5, (index) => Icon(
                            index < (order.rating ?? 0) ? Icons.star_rounded : Icons.star_outline_rounded,
                            color: Colors.amber,
                            size: 20,
                          )),
                          const SizedBox(width: 8),
                          Text(
                            '${order.rating} / 5 ดาว',
                            style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 13),
                          ),
                        ],
                      ),
                      if (order.reviewComment != null && order.reviewComment!.isNotEmpty) ...[
                        const SizedBox(height: 4),
                        Text(
                          'ความคิดเห็นของคุณ: "${order.reviewComment}"',
                          style: const TextStyle(fontSize: 12, color: Colors.black87),
                        ),
                      ],
                    ],
                  ),
                )
              else
                SizedBox(
                  width: double.infinity,
                  child: ElevatedButton.icon(
                    style: ElevatedButton.styleFrom(
                      backgroundColor: Colors.amber.shade700,
                      padding: const EdgeInsets.symmetric(vertical: 12),
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                    ),
                    icon: const Icon(Icons.star_rounded, color: Colors.white),
                    label: const Text(
                      '⭐ ให้คะแนนความพึงพอใจ',
                      style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold),
                    ),
                    onPressed: () {
                      OrderReviewDialog.show(
                        context,
                        order: order,
                        onReviewed: () {
                          ref.read(activeOrderProvider.notifier).watchOrder(orderId);
                        },
                      );
                    },
                  ),
                ),
            ],
            const SizedBox(height: 16),
          ],
        ),
      ),
    );
  }
}
