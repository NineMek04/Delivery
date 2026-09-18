import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/app_theme.dart';
import '../providers/store_orders_provider.dart';
import '../widgets/store_order_card.dart';

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
            : ordersState.error != null
                ? _ErrorState(
                    onRetry: () =>
                        ref.read(storeOrdersProvider.notifier).loadOrders(),
                  )
            : ordersState.orders.isEmpty
                ? _EmptyState()
                : ListView.separated(
                    padding: const EdgeInsets.all(16),
                    itemCount: ordersState.orders.length,
                    separatorBuilder: (_, __) => const SizedBox(height: 12),
                    itemBuilder: (context, index) {
                      final order = ordersState.orders[index];
                      return StoreOrderCard(order: order);
                    },
                  ),
      ),
    );
  }
}

class _ErrorState extends StatelessWidget {
  final VoidCallback onRetry;

  const _ErrorState({required this.onRetry});

  @override
  Widget build(BuildContext context) {
    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.all(24),
      children: [
        const SizedBox(height: 120),
        Icon(
          Icons.cloud_off_outlined,
          size: 72,
          color: AppTheme.errorColor.withValues(alpha: 0.8),
        ),
        const SizedBox(height: 16),
        Text(
          'ไม่สามารถโหลดออเดอร์ได้',
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.titleLarge,
        ),
        const SizedBox(height: 8),
        const Text(
          'ตรวจสอบการเชื่อมต่อแล้วลองอีกครั้ง',
          textAlign: TextAlign.center,
          style: TextStyle(color: AppTheme.textMuted),
        ),
        const SizedBox(height: 20),
        Center(
          child: FilledButton.icon(
            onPressed: onRetry,
            icon: const Icon(Icons.refresh),
            label: const Text('ลองอีกครั้ง'),
          ),
        ),
      ],
    );
  }
}

class _EmptyState extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      children: [
        const SizedBox(height: 120),
        Column(
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
      ],
    );
  }
}
