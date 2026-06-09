import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/app_theme.dart';
import '../../../models/order.dart';
import '../providers/store_orders_provider.dart';

/// StoreOrdersScreen — Real-time incoming order management for store partners.
///
/// Features:
/// - Live SignalR-powered order list (no reload needed)
/// - Accept / Reject buttons per order
/// - Badge clears when screen opens
class StoreOrdersScreen extends ConsumerStatefulWidget {
  const StoreOrdersScreen({super.key});

  @override
  ConsumerState<StoreOrdersScreen> createState() => _StoreOrdersScreenState();
}

class _StoreOrdersScreenState extends ConsumerState<StoreOrdersScreen> {
  @override
  void initState() {
    super.initState();
    // Clear notification badge on open
    WidgetsBinding.instance.addPostFrameCallback((_) {
      ref.read(storeOrdersProvider.notifier).clearBadge();
    });
  }

  @override
  Widget build(BuildContext context) {
    final ordersState = ref.watch(storeOrdersProvider);

    return Scaffold(
      appBar: AppBar(
        title: Row(
          children: [
            const Text('ออเดอร์ที่เข้ามา'),
            if (ordersState.newOrderBadgeCount > 0) ...[
              const SizedBox(width: 8),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                decoration: BoxDecoration(
                  color: AppTheme.errorColor,
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Text(
                  '${ordersState.newOrderBadgeCount} ใหม่',
                  style: const TextStyle(
                    color: Colors.white,
                    fontSize: 12,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
            ],
          ],
        ),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            tooltip: 'รีเฟรช',
            onPressed: () => ref.read(storeOrdersProvider.notifier).loadOrders(),
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () => ref.read(storeOrdersProvider.notifier).loadOrders(),
        child: ordersState.isLoading
            ? const Center(child: CircularProgressIndicator())
            : ordersState.orders.isEmpty
                ? _EmptyState()
                : ListView.separated(
                    padding: const EdgeInsets.all(16),
                    itemCount: ordersState.orders.length,
                    separatorBuilder: (_, __) => const SizedBox(height: 12),
                    itemBuilder: (context, index) {
                      final order = ordersState.orders[index];
                      return _OrderCard(order: order);
                    },
                  ),
      ),
    );
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Order Card
// ─────────────────────────────────────────────────────────────────────────────

class _OrderCard extends ConsumerWidget {
  final OrderDto order;
  const _OrderCard({required this.order});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final status = order.status.toUpperCase();
    final isPending = status == 'CREATED' || status == 'PENDING';
    final isPreparing = status == 'PREPARING';
    final isCancelled = status == 'CANCELLED';

    Color statusColor;
    String statusLabel;
    IconData statusIcon;

    if (isPending) {
      statusColor = const Color(0xFFF59E0B);
      statusLabel = 'รอยืนยัน';
      statusIcon = Icons.hourglass_top;
    } else if (isPreparing) {
      statusColor = AppTheme.primaryColor;
      statusLabel = 'กำลังเตรียม';
      statusIcon = Icons.restaurant;
    } else if (isCancelled) {
      statusColor = AppTheme.errorColor;
      statusLabel = 'ยกเลิกแล้ว';
      statusIcon = Icons.cancel;
    } else {
      statusColor = AppTheme.accentColor;
      statusLabel = status;
      statusIcon = Icons.check_circle;
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
                          child: Text(
                            item.name,
                            style: const TextStyle(fontSize: 13),
                            overflow: TextOverflow.ellipsis,
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
          if (isPending) ...[
            const Divider(height: 1),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
              child: Row(
                children: [
                  Expanded(
                    child: OutlinedButton.icon(
                      onPressed: () => _reject(context, ref),
                      icon: const Icon(Icons.close, size: 16),
                      label: const Text('ปฏิเสธ'),
                      style: OutlinedButton.styleFrom(
                        foregroundColor: AppTheme.errorColor,
                        side: const BorderSide(color: AppTheme.errorColor),
                        padding: const EdgeInsets.symmetric(vertical: 10),
                      ),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    flex: 2,
                    child: FilledButton.icon(
                      onPressed: () => _accept(context, ref),
                      icon: const Icon(Icons.check, size: 16),
                      label: const Text('รับออเดอร์'),
                      style: FilledButton.styleFrom(
                        backgroundColor: AppTheme.primaryColor,
                        padding: const EdgeInsets.symmetric(vertical: 10),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ] else
            const SizedBox(height: 14),
        ],
      ),
    );
  }

  Future<void> _accept(BuildContext context, WidgetRef ref) async {
    await ref.read(storeOrdersProvider.notifier).acceptOrder(order.id);
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('✅ รับออเดอร์แล้ว กำลังเตรียมอาหาร'),
          behavior: SnackBarBehavior.floating,
          backgroundColor: AppTheme.primaryColor,
        ),
      );
    }
  }

  Future<void> _reject(BuildContext context, WidgetRef ref) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('ยืนยันการปฏิเสธ'),
        content: const Text('คุณต้องการปฏิเสธออเดอร์นี้ใช่หรือไม่?'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('ยกเลิก'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(ctx, true),
            style: FilledButton.styleFrom(backgroundColor: AppTheme.errorColor),
            child: const Text('ปฏิเสธ'),
          ),
        ],
      ),
    );
    if (confirmed == true) {
      await ref.read(storeOrdersProvider.notifier).rejectOrder(order.id);
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('ออเดอร์ถูกปฏิเสธแล้ว'),
            behavior: SnackBarBehavior.floating,
          ),
        );
      }
    }
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

// ─────────────────────────────────────────────────────────────────────────────
// Empty State
// ─────────────────────────────────────────────────────────────────────────────

class _EmptyState extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(
            Icons.receipt_long_outlined,
            size: 72,
            color: AppTheme.textMuted.withValues(alpha: 0.5),
          ),
          const SizedBox(height: 16),
          Text(
            'ยังไม่มีออเดอร์',
            style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                  color: AppTheme.textMuted,
                ),
          ),
          const SizedBox(height: 8),
          const Text(
            'ออเดอร์ใหม่จะปรากฏที่นี่แบบเรียลไทม์',
            style: TextStyle(color: AppTheme.textMuted),
          ),
        ],
      ),
    );
  }
}
