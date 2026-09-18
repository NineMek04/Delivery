import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../../app/app_theme.dart';
import '../../../models/order.dart';
import '../providers/store_orders_provider.dart';

class StoreOrderActions extends ConsumerWidget {
  final OrderDto order;
  final bool isProcessing;

  const StoreOrderActions({
    super.key,
    required this.order,
    required this.isProcessing,
  });

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        const Divider(height: 1),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
          child: Row(
            children: [
              Expanded(
                child: OutlinedButton.icon(
                  onPressed: isProcessing ? null : () => _reject(context, ref),
                  icon: isProcessing
                      ? const SizedBox(
                          width: 16,
                          height: 16,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Icon(Icons.close, size: 16),
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
                  onPressed: isProcessing ? null : () => _accept(context, ref),
                  icon: isProcessing
                      ? const SizedBox(
                          width: 16,
                          height: 16,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Icon(Icons.check, size: 16),
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
      ],
    );
  }

  Future<void> _accept(BuildContext context, WidgetRef ref) async {
    final succeeded = await ref.read(storeOrdersProvider.notifier).acceptOrder(order.id);
    if (!succeeded) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('รับออเดอร์ไม่สำเร็จ กรุณาลองใหม่'),
            backgroundColor: AppTheme.errorColor,
          ),
        );
      }
      return;
    }
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
      final succeeded = await ref.read(storeOrdersProvider.notifier).rejectOrder(order.id);
      if (!succeeded) {
        if (context.mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(
              content: Text('ปฏิเสธออเดอร์ไม่สำเร็จ กรุณาลองใหม่'),
              backgroundColor: AppTheme.errorColor,
            ),
          );
        }
        return;
      }
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
}
