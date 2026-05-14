import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Active Delivery Screen — แสดง order ที่กำลังส่งอยู่.
///
/// แสดง:
/// - รายการ order ที่ assign ให้ rider (status: ASSIGNED, PICKED_UP, DELIVERING)
/// - รายละเอียด pickup/dropoff locations
/// - ปุ่มอัปเดตสถานะ (รับของ → กำลังส่ง → ส่งสำเร็จ)
///
/// TODO: ใส่ UI จริงพร้อม order list จาก BackendApi
class ActiveDeliveryScreen extends ConsumerWidget {
  const ActiveDeliveryScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('งานส่งปัจจุบัน'),
      ),
      body: Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(
              Icons.local_shipping_outlined,
              size: 64,
              color: Theme.of(context).colorScheme.primary.withValues(alpha: 0.5),
            ),
            const SizedBox(height: 16),
            Text(
              'ไม่มีงานส่งที่กำลังดำเนินการ',
              style: Theme.of(context).textTheme.bodyLarge,
            ),
            const SizedBox(height: 8),
            Text(
              '[ Active Delivery List Placeholder ]',
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
