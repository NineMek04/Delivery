import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Delivery History Screen — ประวัติการส่งของ Rider.
///
/// แสดง:
/// - รายการ order ที่ส่งสำเร็จแล้ว (status: COMPLETED)
/// - รายการ order ที่ถูกยกเลิก (status: CANCELLED)
/// - Filter ตามวันที่
///
/// TODO: ใส่ UI จริงพร้อม order history list จาก BackendApi
class DeliveryHistoryScreen extends ConsumerWidget {
  const DeliveryHistoryScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('ประวัติการส่ง'),
      ),
      body: Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(
              Icons.history,
              size: 64,
              color: Theme.of(context).colorScheme.primary.withValues(alpha: 0.5),
            ),
            const SizedBox(height: 16),
            Text(
              'ยังไม่มีประวัติการส่ง',
              style: Theme.of(context).textTheme.bodyLarge,
            ),
            const SizedBox(height: 8),
            Text(
              '[ Delivery History List Placeholder ]',
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                color: Theme.of(context).colorScheme.primary,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
