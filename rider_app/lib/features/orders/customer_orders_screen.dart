import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import '../../../core/api/services/order_api_service.dart';
import '../../../core/signalr/customer_signalr_service.dart';
import '../../../models/order.dart';
import '../../../shared/utils/order_status_helper.dart';

final customerOrdersProvider = FutureProvider.autoDispose<List<OrderDto>>((ref) async {
  return ref.read(orderApiServiceProvider).getCustomerOrders();
});

class CustomerOrdersScreen extends ConsumerStatefulWidget {
  const CustomerOrdersScreen({super.key});

  @override
  ConsumerState<CustomerOrdersScreen> createState() =>
      _CustomerOrdersScreenState();
}

class _CustomerOrdersScreenState extends ConsumerState<CustomerOrdersScreen> {
  StreamSubscription<CustomerOrderStatusChangedEvent>? _statusSubscription;

  @override
  void initState() {
    super.initState();
    Future.microtask(_connectRealtime);
  }

  Future<void> _connectRealtime() async {
    try {
      final signalR = ref.read(customerSignalRServiceProvider.notifier);
      await signalR.connect();
      _statusSubscription?.cancel();
      _statusSubscription = signalR.onOrderStatusChanged.listen((_) {
        ref.invalidate(customerOrdersProvider);
      });
    } catch (_) {
      // Pull-to-refresh remains available when realtime connection is down.
    }
  }

  @override
  void dispose() {
    _statusSubscription?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final ordersAsync = ref.watch(customerOrdersProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('ออเดอร์ของฉัน')),
      body: ordersAsync.when(
        data: (orders) => orders.isEmpty
            ? RefreshIndicator(
                onRefresh: () => ref.refresh(customerOrdersProvider.future),
                child: ListView(
                  physics: const AlwaysScrollableScrollPhysics(),
                  children: const [
                    SizedBox(height: 160),
                    Icon(Icons.receipt_long_outlined, size: 64),
                    SizedBox(height: 16),
                    Text(
                      'คุณยังไม่มีรายการสั่งซื้อ',
                      textAlign: TextAlign.center,
                    ),
                  ],
                ),
              )
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
        error: (err, stack) => RefreshIndicator(
          onRefresh: () => ref.refresh(customerOrdersProvider.future),
          child: ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.all(24),
            children: [
              const SizedBox(height: 120),
              const Icon(Icons.cloud_off_outlined, size: 64),
              const SizedBox(height: 16),
              const Text(
                'ไม่สามารถโหลดรายการสั่งซื้อได้',
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 20),
              Center(
                child: FilledButton.icon(
                  onPressed: () => ref.invalidate(customerOrdersProvider),
                  icon: const Icon(Icons.refresh),
                  label: const Text('ลองอีกครั้ง'),
                ),
              ),
            ],
          ),
        ),
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
            Text('สถานะ: ${OrderStatusHelper.label(order.status)}'),
            Text('วันที่: ${order.createdAt != null ? DateFormat('dd/MM/yyyy HH:mm').format(order.createdAt!) : '—'}'),
          ],
        ),
        trailing: const Icon(Icons.chevron_right),
        onTap: () => context.pushNamed('customerTracking', pathParameters: {'orderId': order.id}),
      ),
    );
  }
}
