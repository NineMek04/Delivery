import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import '../../../app/app_theme.dart';
import '../../../core/api/services/order_api_service.dart';
import '../../../models/order.dart';

final customerOrdersProvider = FutureProvider.autoDispose<List<OrderDto>>((ref) async {
  return ref.read(orderApiServiceProvider).getCustomerOrders();
});

class CustomerOrdersScreen extends ConsumerWidget {
  const CustomerOrdersScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final ordersAsync = ref.watch(customerOrdersProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('ออเดอร์ของฉัน')),
      body: ordersAsync.when(
        data: (orders) => orders.isEmpty
            ? const Center(child: Text('คุณยังไม่มีรายการสั่งซื้อ'))
            : RefreshIndicator(
                onRefresh: () => ref.refresh(customerOrdersProvider.future),
                child: ListView.builder(
                  padding: const EdgeInsets.all(16),
                  itemCount: orders.length,
                  itemBuilder: (context, index) {
                    final order = orders[index];
                    return _OrderListTile(order: order);
                  },
                ),
              ),
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (err, stack) => Center(child: Text('เกิดข้อผิดพลาด: $err')),
      ),
    );
  }
}

class _OrderListTile extends StatelessWidget {
  final OrderDto order;

  const _OrderListTile({required this.order});

  @override
  Widget build(BuildContext context) {
    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: ListTile(
        title: Text(
          'ออเดอร์ #${order.trackingCode ?? order.id.substring(0, 8)}',
          style: const TextStyle(fontWeight: FontWeight.bold),
        ),
        subtitle: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('สถานะ: ${order.status}'),
            Text('วันที่: ${order.createdAt != null ? DateFormat('dd/MM/yyyy HH:mm').format(order.createdAt!) : '—'}'),
          ],
        ),
        trailing: const Icon(Icons.chevron_right),
        onTap: () => context.pushNamed('customerTracking', pathParameters: {'orderId': order.id}),
      ),
    );
  }
}
