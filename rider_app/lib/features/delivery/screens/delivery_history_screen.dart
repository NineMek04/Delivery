import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../shared/widgets/loading_overlay.dart';
import '../../../shared/widgets/order_card.dart';
import '../providers/delivery_provider.dart';

/// ประวัติออเดอร์ที่ส่งเสร็จ / ยกเลิก.
class DeliveryHistoryScreen extends ConsumerStatefulWidget {
  const DeliveryHistoryScreen({super.key});

  @override
  ConsumerState<DeliveryHistoryScreen> createState() => _DeliveryHistoryScreenState();
}

class _DeliveryHistoryScreenState extends ConsumerState<DeliveryHistoryScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      ref.read(deliveryNotifierProvider.notifier).loadOrders();
    });
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(deliveryNotifierProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text('ประวัติการส่ง'),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            onPressed: () => ref.read(deliveryNotifierProvider.notifier).loadOrders(),
          ),
        ],
      ),
      body: Stack(
        children: [
          if (state.completedOrders.isEmpty && !state.isLoading)
            const Center(child: Text('ยังไม่มีประวัติการส่ง'))
          else
            RefreshIndicator(
              onRefresh: () => ref.read(deliveryNotifierProvider.notifier).loadOrders(),
              child: ListView.builder(
                padding: const EdgeInsets.all(16),
                itemCount: state.completedOrders.length,
                itemBuilder: (context, index) {
                  return OrderCard(order: state.completedOrders[index]);
                },
              ),
            ),
          if (state.isLoading) const LoadingOverlay(),
        ],
      ),
    );
  }
}
