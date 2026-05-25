import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../shared/utils/order_status_helper.dart';
import '../../../shared/widgets/error_dialog.dart';
import '../../../shared/widgets/loading_overlay.dart';
import '../../../shared/widgets/order_card.dart';
import '../providers/delivery_provider.dart';

/// งานส่งที่กำลังดำเนินการ — อัปเดตสถานะตาม state machine.
class ActiveDeliveryScreen extends ConsumerStatefulWidget {
  const ActiveDeliveryScreen({super.key});

  @override
  ConsumerState<ActiveDeliveryScreen> createState() => _ActiveDeliveryScreenState();
}

class _ActiveDeliveryScreenState extends ConsumerState<ActiveDeliveryScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      ref.read(deliveryNotifierProvider.notifier).loadOrders();
    });
  }

  Future<void> _advanceStatus(String orderId, String currentStatus) async {
    final next = OrderStatusHelper.nextRiderStatus(currentStatus);
    if (next == null) return;

    await ref.read(deliveryNotifierProvider.notifier).updateOrderStatus(orderId, next);
    if (!mounted) return;
    final err = ref.read(deliveryNotifierProvider).error;
    if (err != null) {
      ErrorDialog.show(context, title: 'อัปเดตไม่สำเร็จ', message: err);
    } else {
      ErrorDialog.showSuccess(context, 'อัปเดตสถานะเป็น $next');
    }
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(deliveryNotifierProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text('งานส่งปัจจุบัน'),
        actions: [
          IconButton(
            icon: const Icon(Icons.map_outlined),
            onPressed: () => context.goNamed('tracking'),
          ),
          IconButton(
            icon: const Icon(Icons.refresh),
            onPressed: () => ref.read(deliveryNotifierProvider.notifier).loadOrders(),
          ),
        ],
      ),
      body: Stack(
        children: [
          if (state.activeOrders.isEmpty && !state.isLoading)
            Center(
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Icon(
                    Icons.local_shipping_outlined,
                    size: 64,
                    color: Theme.of(context).colorScheme.primary.withValues(alpha: 0.5),
                  ),
                  const SizedBox(height: 16),
                  const Text('ไม่มีงานส่งที่กำลังดำเนินการ'),
                  const SizedBox(height: 16),
                  OutlinedButton(
                    onPressed: () => context.goNamed('home'),
                    child: const Text('กลับหน้าหลัก'),
                  ),
                ],
              ),
            )
          else
            RefreshIndicator(
              onRefresh: () => ref.read(deliveryNotifierProvider.notifier).loadOrders(),
              child: ListView.builder(
                padding: const EdgeInsets.all(16),
                itemCount: state.activeOrders.length,
                itemBuilder: (context, index) {
                  final order = state.activeOrders[index];
                  final next = OrderStatusHelper.nextRiderStatus(order.status);
                  return OrderCard(
                    order: order,
                    isLoading: state.isUpdating,
                    primaryActionLabel: next != null
                        ? OrderStatusHelper.nextActionLabel(order.status)
                        : null,
                    onPrimaryAction: next != null
                        ? () => _advanceStatus(order.id, order.status)
                        : null,
                  );
                },
              ),
            ),
          if (state.isLoading) const LoadingOverlay(),
        ],
      ),
    );
  }
}
