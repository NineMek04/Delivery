import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import '../../../core/api/services/order_api_service.dart';
import '../../../core/signalr/customer_signalr_service.dart';
import '../../../models/order.dart';
import '../../../shared/utils/order_status_helper.dart';
import '../../../shared/widgets/order_review_dialog.dart';

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
      appBar: AppBar(
        title: const Text('ออเดอร์ของฉัน'),
        actions: [
          IconButton(
            icon: const Icon(Icons.delete_sweep),
            tooltip: 'ล้างประวัติออเดอร์',
            onPressed: () async {
              final confirm = await showDialog<bool>(
                context: context,
                builder: (context) => AlertDialog(
                  title: const Text('ล้างประวัติออเดอร์'),
                  content: const Text('คุณแน่ใจหรือไม่ว่าต้องการล้างประวัติออเดอร์ทั้งหมด?'),
                  actions: [
                    TextButton(
                      onPressed: () => Navigator.pop(context, false),
                      child: const Text('ยกเลิก'),
                    ),
                    TextButton(
                      onPressed: () => Navigator.pop(context, true),
                      child: const Text('ล้างประวัติ', style: TextStyle(color: Colors.red)),
                    ),
                  ],
                ),
              );
              if (confirm == true) {
                try {
                  await ref.read(orderApiServiceProvider).clearCustomerOrders();
                  ref.invalidate(customerOrdersProvider);
                  if (context.mounted) {
                    ScaffoldMessenger.of(context).showSnackBar(
                      const SnackBar(
                        content: Text('ล้างประวัติออเดอร์เรียบร้อยแล้ว'),
                        backgroundColor: Colors.green,
                      ),
                    );
                  }
                } catch (e) {
                  if (context.mounted) {
                    ScaffoldMessenger.of(context).showSnackBar(
                      SnackBar(
                        content: Text('ไม่สามารถล้างประวัติออเดอร์ได้: $e'),
                        backgroundColor: Colors.red,
                      ),
                    );
                  }
                }
              }
            },
          ),
        ],
      ),
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

class _OrderListTile extends ConsumerWidget {
  final OrderDto order;

  const _OrderListTile({required this.order});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  'ออเดอร์ #${order.trackingCode ?? order.id.substring(0, 8)}',
                  style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 15),
                ),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                  decoration: BoxDecoration(
                    color: OrderStatusHelper.statusColor(order.status).withOpacity(0.12),
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: Text(
                    OrderStatusHelper.label(order.status),
                    style: TextStyle(
                      fontSize: 11,
                      fontWeight: FontWeight.bold,
                      color: OrderStatusHelper.statusColor(order.status),
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 6),
            Text(
              'วันที่: ${order.createdAt != null ? DateFormat('dd/MM/yyyy HH:mm').format(order.createdAt!) : '—'}',
              style: const TextStyle(fontSize: 12, color: Colors.grey),
            ),
            if (order.deliveryAddress != null && order.deliveryAddress!.isNotEmpty) ...[
              const SizedBox(height: 4),
              Text(
                'จุดส่ง: ${order.deliveryAddress}',
                style: const TextStyle(fontSize: 12, color: Colors.black87),
              ),
            ],
            if (order.noteToRider != null && order.noteToRider!.isNotEmpty) ...[
              const SizedBox(height: 2),
              Text(
                'ข้อความถึงไรเดอร์: ${order.noteToRider}',
                style: const TextStyle(fontSize: 12, color: Colors.black54),
              ),
            ],
            const SizedBox(height: 8),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                if (order.status == 'COMPLETED') ...[
                  if (order.rating != null)
                    Row(
                      children: [
                        const Icon(Icons.star_rounded, color: Colors.amber, size: 18),
                        const SizedBox(width: 4),
                        Text(
                          '${order.rating}/5 ดาว',
                          style: const TextStyle(fontSize: 12, fontWeight: FontWeight.bold, color: Colors.amber),
                        ),
                      ],
                    )
                  else
                    OutlinedButton.icon(
                      style: OutlinedButton.styleFrom(
                        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                        visualDensity: VisualDensity.compact,
                        side: BorderSide(color: Colors.amber.shade800),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                      ),
                      icon: const Icon(Icons.star_rounded, size: 14, color: Colors.amber),
                      label: Text(
                        'ให้คะแนน',
                        style: TextStyle(fontSize: 12, fontWeight: FontWeight.bold, color: Colors.amber.shade900),
                      ),
                      onPressed: () {
                        OrderReviewDialog.show(
                          context,
                          order: order,
                          onReviewed: () => ref.refresh(customerOrdersProvider.future),
                        );
                      },
                    ),
                ] else ...[
                  const SizedBox.shrink(),
                ],
                TextButton.icon(
                  style: TextButton.styleFrom(
                    visualDensity: VisualDensity.compact,
                    padding: const EdgeInsets.symmetric(horizontal: 8),
                  ),
                  icon: const Icon(Icons.map_outlined, size: 16),
                  label: const Text('ติดตาม', style: TextStyle(fontSize: 12, fontWeight: FontWeight.bold)),
                  onPressed: () => context.pushNamed('customerTracking', pathParameters: {'orderId': order.id}),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
