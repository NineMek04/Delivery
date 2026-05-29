import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/app_theme.dart';
import '../../../core/api/services/customer_address_api_service.dart';
import '../../../models/customer_address.dart';

final customerAddressesProvider = FutureProvider.autoDispose<List<CustomerAddressDto>>((ref) async {
  final service = ref.watch(customerAddressApiServiceProvider);
  final result = await service.getAddresses();
  return result.items;
});

class CustomerAddressesScreen extends ConsumerStatefulWidget {
  const CustomerAddressesScreen({super.key});

  @override
  ConsumerState<CustomerAddressesScreen> createState() => _CustomerAddressesScreenState();
}

class _CustomerAddressesScreenState extends ConsumerState<CustomerAddressesScreen> {
  bool _isUpdating = false;

  Future<void> _setDefault(String id) async {
    setState(() => _isUpdating = true);
    try {
      final service = ref.read(customerAddressApiServiceProvider);
      await service.updateAddress(id, isDefault: true);
      ref.invalidate(customerAddressesProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('ตั้งค่าที่อยู่เริ่มต้นเรียบร้อยแล้ว')),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('เกิดข้อผิดพลาด: $e'), backgroundColor: AppTheme.errorColor),
        );
      }
    } finally {
      if (mounted) setState(() => _isUpdating = false);
    }
  }

  Future<void> _deleteAddress(String id) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('ลบที่อยู่'),
        content: const Text('คุณต้องการลบที่อยู่จัดส่งนี้ใช่หรือไม่?'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('ยกเลิก')),
          TextButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('ลบ', style: TextStyle(color: AppTheme.errorColor)),
          ),
        ],
      ),
    );

    if (confirmed != true) return;

    setState(() => _isUpdating = true);
    try {
      final service = ref.read(customerAddressApiServiceProvider);
      await service.deleteAddress(id);
      ref.invalidate(customerAddressesProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('ลบที่อยู่เรียบร้อยแล้ว')),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('เกิดข้อผิดพลาด: $e'), backgroundColor: AppTheme.errorColor),
        );
      }
    } finally {
      if (mounted) setState(() => _isUpdating = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final addressesAsync = ref.watch(customerAddressesProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text('ที่อยู่จัดส่งของฉัน'),
      ),
      body: Stack(
        children: [
          addressesAsync.when(
            data: (addresses) {
              if (addresses.isEmpty) {
                return Center(
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      const Icon(Icons.location_off_outlined, size: 64, color: AppTheme.textMuted),
                      const SizedBox(height: 16),
                      const Text(
                        'ยังไม่มีที่อยู่จัดส่งที่บันทึกไว้',
                        style: TextStyle(fontSize: 16, color: AppTheme.textMuted),
                      ),
                      const SizedBox(height: 24),
                      ElevatedButton.icon(
                        onPressed: () => context.pushNamed('customerAddressMap'),
                        icon: const Icon(Icons.add_location_alt_outlined),
                        label: const Text('เพิ่มที่อยู่จัดส่งใหม่'),
                      ),
                    ],
                  ),
                );
              }

              return RefreshIndicator(
                onRefresh: () => ref.refresh(customerAddressesProvider.future),
                child: ListView.builder(
                  padding: const EdgeInsets.all(16),
                  itemCount: addresses.length,
                  itemBuilder: (context, index) {
                    final addr = addresses[index];
                    return Card(
                      margin: const EdgeInsets.only(bottom: 12),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(12),
                        side: addr.isDefault
                            ? const BorderSide(color: AppTheme.primaryColor, width: 1.5)
                            : BorderSide.none,
                      ),
                      child: Padding(
                        padding: const EdgeInsets.all(16),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Row(
                              mainAxisAlignment: MainAxisAlignment.spaceBetween,
                              children: [
                                Row(
                                  children: [
                                    Icon(
                                      addr.isDefault ? Icons.stars : Icons.home_outlined,
                                      color: addr.isDefault ? AppTheme.primaryColor : AppTheme.textMuted,
                                    ),
                                    const SizedBox(width: 8),
                                    Text(
                                      addr.name,
                                      style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16),
                                    ),
                                    if (addr.isDefault) ...[
                                      const SizedBox(width: 8),
                                      Container(
                                        padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                                        decoration: BoxDecoration(
                                          color: AppTheme.primaryColor.withValues(alpha: 0.1),
                                          borderRadius: BorderRadius.circular(4),
                                        ),
                                        child: const Text(
                                          'ที่อยู่เริ่มต้น',
                                          style: TextStyle(
                                            fontSize: 10,
                                            color: AppTheme.primaryColor,
                                            fontWeight: FontWeight.bold,
                                          ),
                                        ),
                                      ),
                                    ],
                                  ],
                                ),
                                IconButton(
                                  icon: const Icon(Icons.delete_outline, color: AppTheme.errorColor, size: 20),
                                  onPressed: () => _deleteAddress(addr.id),
                                ),
                              ],
                            ),
                            const Divider(height: 16),
                            Text(
                              addr.addressLine1,
                              style: const TextStyle(fontSize: 14),
                            ),
                            if (addr.addressLine2 != null && addr.addressLine2!.isNotEmpty) ...[
                              const SizedBox(height: 4),
                              Text(
                                addr.addressLine2!,
                                style: const TextStyle(fontSize: 14, color: AppTheme.textMuted),
                              ),
                            ],
                            const SizedBox(height: 4),
                            Text(
                              '${addr.state}, ${addr.city} ${addr.postalCode}',
                              style: const TextStyle(fontSize: 14),
                            ),
                            if (!addr.isDefault) ...[
                              const SizedBox(height: 12),
                              Align(
                                alignment: Alignment.centerRight,
                                child: TextButton.icon(
                                  onPressed: _isUpdating ? null : () => _setDefault(addr.id),
                                  icon: const Icon(Icons.check_circle_outline, size: 16),
                                  label: const Text('ตั้งเป็นที่อยู่เริ่มต้น', style: TextStyle(fontSize: 13)),
                                ),
                              ),
                            ],
                          ],
                        ),
                      ),
                    );
                  },
                ),
              );
            },
            loading: () => const Center(child: CircularProgressIndicator()),
            error: (e, _) => Center(
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  const Icon(Icons.error_outline, size: 48, color: AppTheme.errorColor),
                  const SizedBox(height: 16),
                  Text('เกิดข้อผิดพลาด: $e'),
                  const SizedBox(height: 16),
                  ElevatedButton(
                    onPressed: () => ref.refresh(customerAddressesProvider),
                    child: const Text('ลองใหม่'),
                  ),
                ],
              ),
            ),
          ),
          if (_isUpdating)
            Container(
              color: Colors.black26,
              child: const Center(child: CircularProgressIndicator()),
            ),
        ],
      ),
      floatingActionButton: addressesAsync.maybeWhen(
        data: (addresses) => addresses.isNotEmpty
            ? FloatingActionButton.extended(
                onPressed: () => context.pushNamed('customerAddressMap'),
                icon: const Icon(Icons.add_location_alt_outlined),
                label: const Text('เพิ่มที่อยู่ใหม่'),
              )
            : null,
        orElse: () => null,
      ),
    );
  }
}
