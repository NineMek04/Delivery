import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../../app/app_theme.dart';
import '../../../models/order.dart';
import '../providers/store_orders_provider.dart';
import 'store_order_actions.dart';

class StoreOrderCard extends ConsumerWidget {
  final OrderDto order;
  const StoreOrderCard({super.key, required this.order});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final status = order.status.toUpperCase();
    final isPending = status == 'CREATED';
    final isCancelled = status == 'CANCELLED';
    final isProcessing = ref.watch(
      storeOrdersProvider.select(
        (state) => state.processingOrderIds.contains(order.id),
      ),
    );

    Color statusColor;
    String statusLabel;
    IconData statusIcon;

    if (isPending) {
      statusColor = const Color(0xFFF59E0B);
      statusLabel = 'รอยืนยัน';
      statusIcon = Icons.hourglass_top;
    } else if (isCancelled) {
      statusColor = AppTheme.errorColor;
      statusLabel = 'ยกเลิกแล้ว';
      statusIcon = Icons.cancel;
    } else {
      final s = status.toUpperCase();
      if (s == 'COMPLETED') {
        statusColor = AppTheme.accentColor;
        statusLabel = 'ส่งสำเร็จ';
        statusIcon = Icons.check_circle;
      } else if (s == 'MATCHING' || s == 'OFFERING') {
        statusColor = AppTheme.primaryColor;
        statusLabel = 'รับออเดอร์แล้ว (กำลังหาคนขับ)';
        statusIcon = Icons.search;
      } else if (s == 'ASSIGNED' || s == 'PICKING_UP') {
        statusColor = Colors.blue;
        statusLabel = 'คนขับกำลังมารับ';
        statusIcon = Icons.delivery_dining;
      } else if (s == 'DELIVERING') {
        statusColor = Colors.purple;
        statusLabel = 'กำลังจัดส่ง';
        statusIcon = Icons.local_shipping;
      } else {
        statusColor = AppTheme.accentColor;
        statusLabel = status;
        statusIcon = Icons.check_circle;
      }
    }

    final totalItems = order.items.fold<int>(0, (sum, i) => sum + i.quantity);
    final totalPrice = order.items.fold<double>(0, (sum, i) => sum + i.totalPrice) + order.deliveryFee;

    return Container(
      decoration: BoxDecoration(
        color: AppTheme.surfaceCard,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(
          color: isPending
              ? const Color(0xFFF59E0B).withValues(alpha: 0.4)
              : AppTheme.borderColor,
          width: isPending ? 1.5 : 1,
        ),
        boxShadow: isPending
            ? [
                BoxShadow(
                  color: const Color(0xFFF59E0B).withValues(alpha: 0.15),
                  blurRadius: 12,
                  offset: const Offset(0, 4),
                ),
              ]
            : null,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // ── Header ───────────────────────────────────────────────
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 14, 12, 0),
            child: Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        order.refNumber != null
                            ? 'ออเดอร์ #${order.refNumber}'
                            : 'ออเดอร์ ${order.id.substring(0, 8).toUpperCase()}',
                        style: const TextStyle(
                          fontWeight: FontWeight.w700,
                          fontSize: 15,
                        ),
                      ),
                      if (order.createdAt != null)
                        Text(
                          _formatTime(order.createdAt!),
                          style: const TextStyle(
                            fontSize: 12,
                            color: AppTheme.textMuted,
                          ),
                        ),
                    ],
                  ),
                ),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                  decoration: BoxDecoration(
                    color: statusColor.withValues(alpha: 0.15),
                    borderRadius: BorderRadius.circular(20),
                    border: Border.all(color: statusColor.withValues(alpha: 0.3)),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(statusIcon, size: 13, color: statusColor),
                      const SizedBox(width: 4),
                      Text(
                        statusLabel,
                        style: TextStyle(
                          fontSize: 12,
                          fontWeight: FontWeight.w600,
                          color: statusColor,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 12),

          // ── Items ─────────────────────────────────────────────────
          if (order.items.isNotEmpty)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: order.items.map((item) {
                  return Padding(
                    padding: const EdgeInsets.only(bottom: 4),
                    child: Row(
                      children: [
                        Container(
                          width: 22,
                          height: 22,
                          alignment: Alignment.center,
                          decoration: BoxDecoration(
                            color: AppTheme.primaryColor.withValues(alpha: 0.15),
                            borderRadius: BorderRadius.circular(6),
                          ),
                          child: Text(
                            '${item.quantity}',
                            style: const TextStyle(
                              fontSize: 11,
                              fontWeight: FontWeight.w700,
                              color: AppTheme.primaryColor,
                            ),
                          ),
                        ),
                        const SizedBox(width: 8),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                item.name,
                                style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600),
                                overflow: TextOverflow.ellipsis,
                              ),
                              if (item.optionsDescription != null && item.optionsDescription!.isNotEmpty)
                                Text(
                                  item.optionsDescription!,
                                  style: const TextStyle(fontSize: 11, color: AppTheme.textMuted),
                                ),
                              if (item.notes != null && item.notes!.isNotEmpty)
                                Text(
                                  'โน้ต: ${item.notes}',
                                  style: const TextStyle(fontSize: 11, color: Colors.orange, fontWeight: FontWeight.w500),
                                ),
                            ],
                          ),
                        ),
                        Text(
                          '฿${item.totalPrice.toStringAsFixed(0)}',
                          style: const TextStyle(
                            fontSize: 13,
                            color: AppTheme.textMuted,
                          ),
                        ),
                      ],
                    ),
                  );
                }).toList(),
              ),
            ),

          if (order.noteToShop != null && order.noteToShop!.isNotEmpty)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
              child: Container(
                width: double.infinity,
                padding: const EdgeInsets.all(10),
                decoration: BoxDecoration(
                  color: Colors.orange.shade50,
                  borderRadius: BorderRadius.circular(8),
                  border: Border.all(color: Colors.orange.shade200),
                ),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Icon(Icons.restaurant, size: 18, color: Colors.orange.shade800),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            'หมายเหตุจากลูกค้าถึงร้านค้า:',
                            style: TextStyle(
                              fontSize: 11,
                              fontWeight: FontWeight.bold,
                              color: Colors.orange.shade900,
                            ),
                          ),
                          const SizedBox(height: 2),
                          Text(
                            order.noteToShop!,
                            style: TextStyle(
                              fontSize: 13,
                              fontWeight: FontWeight.w600,
                              color: Colors.orange.shade900,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ),

          if (order.rating != null)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
              child: Row(
                children: [
                  const Icon(Icons.star_rounded, size: 16, color: Colors.amber),
                  const SizedBox(width: 4),
                  Text(
                    'คะแนนความพึงพอใจ: ${order.rating}/5 ดาว ${order.reviewComment != null && order.reviewComment!.isNotEmpty ? "(${order.reviewComment})" : ""}',
                    style: const TextStyle(fontSize: 12, color: Colors.amber, fontWeight: FontWeight.w600),
                  ),
                ],
              ),
            ),

          // ── Footer ────────────────────────────────────────────────
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 10, 16, 4),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  '$totalItems รายการ • ฿${totalPrice.toStringAsFixed(0)}',
                  style: const TextStyle(
                    fontWeight: FontWeight.w600,
                    fontSize: 14,
                  ),
                ),
              ],
            ),
          ),

          // ── Action Buttons (only for pending orders) ───────────────
          if (isPending)
            StoreOrderActions(order: order, isProcessing: isProcessing)
          else
            const SizedBox(height: 14),
        ],
      ),
    );
  }

  String _formatTime(DateTime dt) {
    final now = DateTime.now();
    final diff = now.difference(dt);
    if (diff.inMinutes < 1) return 'เมื่อกี้';
    if (diff.inMinutes < 60) return '${diff.inMinutes} นาทีที่แล้ว';
    if (diff.inHours < 24) return '${diff.inHours} ชั่วโมงที่แล้ว';
    return '${dt.day}/${dt.month} ${dt.hour}:${dt.minute.toString().padLeft(2, '0')}';
  }
}
