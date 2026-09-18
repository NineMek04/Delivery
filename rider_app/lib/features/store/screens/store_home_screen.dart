import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/app_theme.dart';
import '../../../models/shop.dart';
import '../providers/store_providers.dart';
import '../widgets/store_menu_card.dart';
import '../widgets/store_menu_form_sheet.dart';

/// Store Home Screen — Page 1: Manage menu items.
///
/// Features:
/// - 2-column card grid of menu items
/// - Add menu: modal form (name, price, description, image, options, map pin)
/// - Delete menu: checkbox mode for batch deletion
/// - Edit menu: radio mode to select and edit via pre-filled form
class StoreHomeScreen extends ConsumerStatefulWidget {
  const StoreHomeScreen({super.key});

  @override
  ConsumerState<StoreHomeScreen> createState() => _StoreHomeScreenState();
}

class _StoreHomeScreenState extends ConsumerState<StoreHomeScreen> {
  StoreMenuMode _mode = StoreMenuMode.view;
  final Set<String> _selectedForDelete = {};
  String? _selectedForEdit;

  @override
  Widget build(BuildContext context) {
    final menuAsync = ref.watch(menuItemsProvider);
    final shop = ref.watch(currentShopProvider);

    return Scaffold(
      appBar: AppBar(
        title: shop.when(
          data: (s) => Text(s?.name ?? 'ร้านค้าของฉัน'),
          loading: () => const Text('กำลังโหลด...'),
          error: (_, __) => const Text('ร้านค้าของฉัน'),
        ),
        actions: [
          if (_mode == StoreMenuMode.delete)
            TextButton(
              onPressed: _selectedForDelete.isEmpty ? null : _confirmDelete,
              child: Text(
                'ลบ (${_selectedForDelete.length})',
                style: const TextStyle(color: AppTheme.errorColor),
              ),
            ),
          if (_mode != StoreMenuMode.view)
            IconButton(
              onPressed: () => setState(() {
                _mode = StoreMenuMode.view;
                _selectedForDelete.clear();
                _selectedForEdit = null;
              }),
              icon: const Icon(Icons.close),
            ),
        ],
      ),
      body: Column(
        children: [
          // Action buttons bar
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: Row(
              children: [
                Expanded(
                  child: StoreActionChip(
                    icon: Icons.add_circle_outline,
                    label: 'เพิ่มเมนู',
                    color: AppTheme.accentColor,
                    onTap: () => _showMenuForm(context),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: StoreActionChip(
                    icon: Icons.edit_outlined,
                    label: 'แก้ไข',
                    color: AppTheme.primaryColor,
                    isActive: _mode == StoreMenuMode.edit,
                    onTap: () => setState(() {
                      _mode = _mode == StoreMenuMode.edit ? StoreMenuMode.view : StoreMenuMode.edit;
                      _selectedForDelete.clear();
                      _selectedForEdit = null;
                    }),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: StoreActionChip(
                    icon: Icons.delete_outline,
                    label: 'ลบ',
                    color: AppTheme.errorColor,
                    isActive: _mode == StoreMenuMode.delete,
                    onTap: () => setState(() {
                      _mode = _mode == StoreMenuMode.delete ? StoreMenuMode.view : StoreMenuMode.delete;
                      _selectedForDelete.clear();
                      _selectedForEdit = null;
                    }),
                  ),
                ),
              ],
            ),
          ),
          // Menu items grid
          Expanded(
            child: menuAsync.when(
              data: (items) {
                if (items.isEmpty) {
                  return Center(
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Icon(Icons.restaurant_menu, size: 80, color: AppTheme.textMuted),
                        const SizedBox(height: 16),
                        Text(
                          'ยังไม่มีเมนูสินค้า',
                          style: Theme.of(context).textTheme.titleLarge?.copyWith(
                                color: AppTheme.textMuted,
                              ),
                        ),
                        const SizedBox(height: 8),
                        Text(
                          'กดปุ่ม "เพิ่มเมนู" เพื่อเริ่มเพิ่มสินค้า',
                          style: Theme.of(context).textTheme.bodyMedium,
                        ),
                      ],
                    ),
                  );
                }
                return GridView.builder(
                  padding: const EdgeInsets.all(16),
                  gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                    crossAxisCount: 2,
                    crossAxisSpacing: 12,
                    mainAxisSpacing: 12,
                    childAspectRatio: 0.75,
                  ),
                  itemCount: items.length,
                  itemBuilder: (context, index) {
                    final item = items[index];
                    return StoreMenuCard(
                      item: item,
                      mode: _mode,
                      isSelectedForDelete: _selectedForDelete.contains(item.id),
                      isSelectedForEdit: _selectedForEdit == item.id,
                      onDeleteToggle: () {
                        setState(() {
                          if (_selectedForDelete.contains(item.id)) {
                            _selectedForDelete.remove(item.id);
                          } else {
                            _selectedForDelete.add(item.id);
                          }
                        });
                      },
                      onEditSelect: () {
                        setState(() => _selectedForEdit = item.id);
                        _showMenuForm(context, existingItem: item);
                      },
                    );
                  },
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
                    const SizedBox(height: 8),
                    ElevatedButton(
                      onPressed: () => ref.read(menuItemsProvider.notifier).refresh(),
                      child: const Text('ลองใหม่'),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _confirmDelete() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('ยืนยันการลบ'),
        content: Text('ต้องการลบ ${_selectedForDelete.length} รายการ?'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('ยกเลิก')),
          TextButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('ลบ', style: TextStyle(color: AppTheme.errorColor)),
          ),
        ],
      ),
    );

    if (confirmed == true && mounted) {
      try {
        await ref.read(menuItemsProvider.notifier).deleteItems(_selectedForDelete.toList());
        if (mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('ลบเมนูสินค้าเรียบร้อยแล้ว')),
          );
        }
      } catch (e) {
        if (mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Text('เกิดข้อผิดพลาดในการลบ: $e'),
              backgroundColor: AppTheme.errorColor,
            ),
          );
        }
      }
      setState(() {
        _selectedForDelete.clear();
        _mode = StoreMenuMode.view;
      });
    }
  }

  void _showMenuForm(BuildContext context, {MenuItemDto? existingItem}) {
    debugPrint('[StoreHomeScreen] _showMenuForm called');
    final shopAsync = ref.read(currentShopProvider);
    
    if (shopAsync.isLoading) {
      debugPrint('[StoreHomeScreen] shop is still loading');
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('กำลังโหลดข้อมูลร้านค้า... กรุณาลองใหม่ในสักครู่')),
      );
      return;
    }
    
    if (shopAsync.hasError) {
      debugPrint('[StoreHomeScreen] shop load failed: ${shopAsync.error}');
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('โหลดข้อมูลร้านค้าล้มเหลว: ${shopAsync.error}')),
      );
      return;
    }

    final shop = shopAsync.value;
    if (shop == null) {
      debugPrint('[StoreHomeScreen] shop is null');
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('ไม่พบข้อมูลร้านค้าสำหรับบัญชีนี้')),
      );
      return;
    }

    debugPrint('[StoreHomeScreen] Opening form for shop: ${shop.name} (${shop.id})');
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: AppTheme.surfaceCard,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
      ),
      builder: (ctx) => StoreMenuFormSheet(
        shopId: shop.id,
        existingItem: existingItem,
        shopLat: shop.lat,
        shopLng: shop.lng,
        onSave: (data) async {
          debugPrint('[StoreHomeScreen] onSave callback triggered for item: ${data['Name']}');
          if (existingItem != null) {
            await ref.read(menuItemsProvider.notifier).updateItem(existingItem.id, data);
          } else {
            await ref.read(menuItemsProvider.notifier).addItem(data);
          }
          if (mounted) {
            setState(() {
              _mode = StoreMenuMode.view;
              _selectedForEdit = null;
            });
          }
        },
      ),
    );
  }
}
