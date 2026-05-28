import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';
import 'package:image_picker/image_picker.dart';

import '../../../app/app_theme.dart';
import '../../../models/shop.dart';
import '../providers/store_providers.dart';

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

enum _MenuMode { view, delete, edit }

class _StoreHomeScreenState extends ConsumerState<StoreHomeScreen> {
  _MenuMode _mode = _MenuMode.view;
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
          if (_mode == _MenuMode.delete)
            TextButton(
              onPressed: _selectedForDelete.isEmpty ? null : _confirmDelete,
              child: Text(
                'ลบ (${_selectedForDelete.length})',
                style: const TextStyle(color: AppTheme.errorColor),
              ),
            ),
          if (_mode != _MenuMode.view)
            IconButton(
              onPressed: () => setState(() {
                _mode = _MenuMode.view;
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
                  child: _ActionChip(
                    icon: Icons.add_circle_outline,
                    label: 'เพิ่มเมนู',
                    color: AppTheme.accentColor,
                    onTap: () => _showMenuForm(context),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: _ActionChip(
                    icon: Icons.edit_outlined,
                    label: 'แก้ไข',
                    color: AppTheme.primaryColor,
                    isActive: _mode == _MenuMode.edit,
                    onTap: () => setState(() {
                      _mode = _mode == _MenuMode.edit ? _MenuMode.view : _MenuMode.edit;
                      _selectedForDelete.clear();
                      _selectedForEdit = null;
                    }),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: _ActionChip(
                    icon: Icons.delete_outline,
                    label: 'ลบ',
                    color: AppTheme.errorColor,
                    isActive: _mode == _MenuMode.delete,
                    onTap: () => setState(() {
                      _mode = _mode == _MenuMode.delete ? _MenuMode.view : _MenuMode.delete;
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
                    return _MenuCard(
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
        _mode = _MenuMode.view;
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
      builder: (ctx) => _MenuFormSheet(
        shopId: shop.id,
        existingItem: existingItem,
        shopLat: shop.lat,
        shopLng: shop.lng,
        onSave: (data) async {
          debugPrint('[StoreHomeScreen] onSave callback triggered with data: $data');
          if (existingItem != null) {
            await ref.read(menuItemsProvider.notifier).updateItem(existingItem.id, data);
          } else {
            await ref.read(menuItemsProvider.notifier).addItem(data);
          }
          if (mounted) {
            setState(() {
              _mode = _MenuMode.view;
              _selectedForEdit = null;
            });
          }
        },
      ),
    );
  }
}

// ═══════════════════════════════════════════════════════════════════
// Menu Card Widget
// ═══════════════════════════════════════════════════════════════════
class _MenuCard extends StatelessWidget {
  final MenuItemDto item;
  final _MenuMode mode;
  final bool isSelectedForDelete;
  final bool isSelectedForEdit;
  final VoidCallback onDeleteToggle;
  final VoidCallback onEditSelect;

  const _MenuCard({
    required this.item,
    required this.mode,
    required this.isSelectedForDelete,
    required this.isSelectedForEdit,
    required this.onDeleteToggle,
    required this.onEditSelect,
  });

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: mode == _MenuMode.delete
          ? onDeleteToggle
          : mode == _MenuMode.edit
              ? onEditSelect
              : null,
      behavior: HitTestBehavior.opaque,
      child: Card(
        clipBehavior: Clip.antiAlias,
        elevation: isSelectedForDelete || isSelectedForEdit ? 4 : 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16),
          side: (isSelectedForDelete || isSelectedForEdit)
              ? BorderSide(
                  color: isSelectedForDelete ? AppTheme.errorColor : AppTheme.primaryColor,
                  width: 2,
                )
              : BorderSide.none,
        ),
        child: Stack(
          children: [
            Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // Image
                Expanded(
                  flex: 3,
                  child: Container(
                    width: double.infinity,
                    color: AppTheme.surfaceElevated,
                    child: _buildImage(item.imageUrl),
                  ),
                ),
                // Details
                Expanded(
                  flex: 2,
                  child: Padding(
                    padding: const EdgeInsets.all(10),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          item.name,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        const SizedBox(height: 4),
                        Text(
                          '฿${item.price.toStringAsFixed(0)}',
                          style: Theme.of(context).textTheme.bodyLarge?.copyWith(
                                color: AppTheme.accentColor,
                                fontWeight: FontWeight.w700,
                              ),
                        ),
                        if (item.description != null && item.description!.isNotEmpty) ...[
                          const SizedBox(height: 2),
                          Text(
                            item.description!,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                                  fontSize: 12,
                                ),
                          ),
                        ],
                      ],
                    ),
                  ),
                ),
              ],
            ),
            // Selection overlay
            if (mode == _MenuMode.delete)
              Positioned(
                top: 8,
                right: 8,
                child: IgnorePointer(
                  child: Container(
                    decoration: BoxDecoration(
                      color: AppTheme.surfaceDark.withValues(alpha: 0.7),
                      borderRadius: BorderRadius.circular(4),
                    ),
                    child: Checkbox(
                      value: isSelectedForDelete,
                      onChanged: (_) {},
                      activeColor: AppTheme.errorColor,
                    ),
                  ),
                ),
              ),
            if (mode == _MenuMode.edit)
              Positioned(
                top: 8,
                right: 8,
                child: IgnorePointer(
                  child: Container(
                    decoration: BoxDecoration(
                      color: AppTheme.surfaceDark.withValues(alpha: 0.7),
                      borderRadius: BorderRadius.circular(4),
                    ),
                    child: Radio<String>(
                      value: item.id,
                      groupValue: isSelectedForEdit ? item.id : null,
                      onChanged: (_) {},
                      activeColor: AppTheme.primaryColor,
                    ),
                  ),
                ),
              ),
            // Options badge
            if (item.options != null && item.options!.isNotEmpty)
              Positioned(
                top: 8,
                left: 8,
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                  decoration: BoxDecoration(
                    color: AppTheme.primaryColor.withValues(alpha: 0.9),
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: Text(
                    '+${item.options!.length} ตัวเลือก',
                    style: const TextStyle(fontSize: 10, color: Colors.white),
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }

  Widget _buildImage(String? url) {
    if (url == null || url.isEmpty) {
      return const Center(
        child: Icon(Icons.fastfood, size: 48, color: AppTheme.textMuted),
      );
    }
    if (url.startsWith('data:image')) {
      try {
        final base64Part = url.split(',').last;
        return Image.memory(
          base64Decode(base64Part),
          fit: BoxFit.cover,
          errorBuilder: (_, __, ___) => const Center(
            child: Icon(Icons.broken_image, size: 48, color: AppTheme.textMuted),
          ),
        );
      } catch (e) {
        return const Center(
          child: Icon(Icons.broken_image, size: 48, color: AppTheme.textMuted),
        );
      }
    }
    return Image.network(
      url,
      fit: BoxFit.cover,
      errorBuilder: (_, __, ___) => const Center(
        child: Icon(Icons.fastfood, size: 48, color: AppTheme.textMuted),
      ),
    );
  }
}

// ═══════════════════════════════════════════════════════════════════
// Action Chip Button
// ═══════════════════════════════════════════════════════════════════
class _ActionChip extends StatelessWidget {
  final IconData icon;
  final String label;
  final Color color;
  final VoidCallback onTap;
  final bool isActive;

  const _ActionChip({
    required this.icon,
    required this.label,
    required this.color,
    required this.onTap,
    this.isActive = false,
  });

  @override
  Widget build(BuildContext context) {
    return Material(
      color: isActive ? color.withValues(alpha: 0.2) : AppTheme.surfaceCard,
      borderRadius: BorderRadius.circular(12),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(12),
        child: Container(
          padding: const EdgeInsets.symmetric(vertical: 10),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(icon, size: 18, color: color),
              const SizedBox(width: 4),
              Text(label, style: TextStyle(color: color, fontSize: 13, fontWeight: FontWeight.w600)),
            ],
          ),
        ),
      ),
    );
  }
}

// ═══════════════════════════════════════════════════════════════════
// Menu Form Bottom Sheet
// ═══════════════════════════════════════════════════════════════════
class _MenuFormSheet extends StatefulWidget {
  final String shopId;
  final MenuItemDto? existingItem;
  final double? shopLat;
  final double? shopLng;
  final Future<void> Function(Map<String, dynamic> data) onSave;

  const _MenuFormSheet({
    required this.shopId,
    this.existingItem,
    this.shopLat,
    this.shopLng,
    required this.onSave,
  });

  @override
  State<_MenuFormSheet> createState() => _MenuFormSheetState();
}

class _MenuFormSheetState extends State<_MenuFormSheet> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _nameController;
  late final TextEditingController _priceController;
  late final TextEditingController _descriptionController;
  late final TextEditingController _imageUrlController;
  late final TextEditingController _optionNameController;

  bool _isSaving = false;
  final ImagePicker _picker = ImagePicker();
  bool _isPickingImage = false;

  @override
  void initState() {
    super.initState();
    final item = widget.existingItem;
    _nameController = TextEditingController(text: item?.name ?? '');
    _priceController = TextEditingController(text: item != null ? item.price.toStringAsFixed(0) : '');
    _descriptionController = TextEditingController(text: item?.description ?? '');
    _imageUrlController = TextEditingController(text: item?.imageUrl ?? '');
    _optionNameController = TextEditingController();
  }

  Future<void> _pickImage() async {
    setState(() => _isPickingImage = true);
    try {
      final XFile? pickedFile = await _picker.pickImage(
        source: ImageSource.gallery,
        maxWidth: 800,
        maxHeight: 800,
        imageQuality: 85,
      );
      if (pickedFile != null) {
        final bytes = await pickedFile.readAsBytes();
        final base64String = base64Encode(bytes);
        setState(() {
          _imageUrlController.text = 'data:image/png;base64,$base64String';
        });
      }
    } catch (e) {
      debugPrint('[MenuFormSheet] Error picking image: $e');
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('ไม่สามารถเลือกรูปภาพได้: $e')),
        );
      }
    } finally {
      if (mounted) setState(() => _isPickingImage = false);
    }
  }

  @override
  void dispose() {
    _nameController.dispose();
    _priceController.dispose();
    _descriptionController.dispose();
    _imageUrlController.dispose();
    _optionNameController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final bottomInset = MediaQuery.of(context).viewInsets.bottom;
    final isEditing = widget.existingItem != null;

    return Padding(
      padding: EdgeInsets.only(bottom: bottomInset),
      child: DraggableScrollableSheet(
        initialChildSize: 0.85,
        maxChildSize: 0.95,
        minChildSize: 0.5,
        expand: false,
        builder: (context, scrollController) {
          return SingleChildScrollView(
            controller: scrollController,
            padding: const EdgeInsets.all(24),
            child: Form(
              key: _formKey,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // Handle bar
                  Center(
                    child: Container(
                      width: 40,
                      height: 4,
                      margin: const EdgeInsets.only(bottom: 16),
                      decoration: BoxDecoration(
                        color: AppTheme.textMuted,
                        borderRadius: BorderRadius.circular(2),
                      ),
                    ),
                  ),
                  Text(
                    isEditing ? 'แก้ไขเมนู' : 'เพิ่มเมนูใหม่',
                    style: Theme.of(context).textTheme.headlineMedium,
                  ),
                  const SizedBox(height: 24),

                  // 1. Image Selection
                  const Text(
                    'รูปภาพเมนูสินค้า',
                    style: TextStyle(fontWeight: FontWeight.w600, fontSize: 14),
                  ),
                  const SizedBox(height: 8),
                  if (_imageUrlController.text.isNotEmpty) ...[
                    Container(
                      height: 150,
                      width: double.infinity,
                      decoration: BoxDecoration(
                        color: AppTheme.surfaceElevated,
                        borderRadius: BorderRadius.circular(16),
                        border: Border.all(color: AppTheme.textMuted.withValues(alpha: 0.3)),
                      ),
                      clipBehavior: Clip.antiAlias,
                      child: Stack(
                        children: [
                          Positioned.fill(
                            child: _imageUrlController.text.startsWith('data:image')
                                ? Image.memory(
                                    base64Decode(_imageUrlController.text.split(',').last),
                                    fit: BoxFit.cover,
                                  )
                                : Image.network(
                                    _imageUrlController.text,
                                    fit: BoxFit.cover,
                                    errorBuilder: (_, __, ___) => const Center(
                                      child: Icon(Icons.broken_image, size: 48, color: AppTheme.textMuted),
                                    ),
                                  ),
                          ),
                          Positioned(
                            top: 8,
                            right: 8,
                            child: CircleAvatar(
                              backgroundColor: AppTheme.surfaceDark.withValues(alpha: 0.7),
                              child: IconButton(
                                icon: const Icon(Icons.delete, color: AppTheme.errorColor),
                                onPressed: () {
                                  setState(() {
                                    _imageUrlController.clear();
                                  });
                                },
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 12),
                  ],
                  SizedBox(
                    width: double.infinity,
                    child: OutlinedButton.icon(
                      onPressed: _isPickingImage ? null : _pickImage,
                      icon: _isPickingImage
                          ? const SizedBox(
                              height: 18,
                              width: 18,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(Icons.add_photo_alternate_outlined),
                      label: Text(_imageUrlController.text.isEmpty
                          ? 'เลือกรูปภาพจากเครื่อง'
                          : 'เปลี่ยนรูปภาพใหม่'),
                      style: OutlinedButton.styleFrom(
                        padding: const EdgeInsets.symmetric(vertical: 12),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(12),
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(height: 16),

                  // 2. Name
                  TextFormField(
                    controller: _nameController,
                    decoration: const InputDecoration(
                      labelText: 'ชื่อเมนู *',
                      prefixIcon: Icon(Icons.restaurant_menu),
                    ),
                    validator: (v) {
                      if (v == null || v.trim().isEmpty) return 'กรุณากรอกชื่อเมนู';
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),

                  // 3. Price
                  TextFormField(
                    controller: _priceController,
                    keyboardType: TextInputType.number,
                    decoration: const InputDecoration(
                      labelText: 'ราคา (บาท) *',
                      prefixIcon: Icon(Icons.attach_money),
                    ),
                    validator: (v) {
                      if (v == null || v.trim().isEmpty) return 'กรุณากรอกราคา';
                      final price = double.tryParse(v);
                      if (price == null || price <= 0) return 'ราคาต้องมากกว่า 0';
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),

                  // 4. Description
                  TextFormField(
                    controller: _descriptionController,
                    maxLines: 3,
                    decoration: const InputDecoration(
                      labelText: 'รายละเอียดเมนู (ไม่บังคับ)',
                      prefixIcon: Icon(Icons.description_outlined),
                    ),
                  ),
                  const SizedBox(height: 16),

                  // 5. Options (simple text)
                  TextFormField(
                    controller: _optionNameController,
                    decoration: const InputDecoration(
                      labelText: 'ออฟชั่นเสริม (ไม่บังคับ เช่น ไซส์, ท็อปปิ้ง)',
                      prefixIcon: Icon(Icons.add_circle_outline),
                    ),
                  ),
                  const SizedBox(height: 16),

                  const SizedBox(height: 24),

                  // Save button
                  SizedBox(
                    width: double.infinity,
                    child: ElevatedButton(
                      onPressed: _isSaving ? null : _submit,
                      child: _isSaving
                          ? const SizedBox(
                              height: 20,
                              width: 20,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : Text(isEditing ? 'บันทึกการแก้ไข' : 'เพิ่มเมนู'),
                    ),
                  ),
                  const SizedBox(height: 16),
                ],
              ),
            ),
          );
        },
      ),
    );
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;

    setState(() => _isSaving = true);

    try {
      final data = <String, dynamic>{
        'Name': _nameController.text.trim(),
        'Price': double.parse(_priceController.text.trim()),
        'ShopId': widget.shopId,
      };

      if (_descriptionController.text.trim().isNotEmpty) {
        data['Description'] = _descriptionController.text.trim();
      }
      if (_imageUrlController.text.trim().isNotEmpty) {
        data['ImageUrl'] = _imageUrlController.text.trim();
      }
      if (_optionNameController.text.trim().isNotEmpty) {
        data['Options'] = [
          {
            'Name': _optionNameController.text.trim(),
            'Required': false,
            'MaxSelections': 1,
            'Items': <Map<String, dynamic>>[],
          }
        ];
      }

      await widget.onSave(data);
      if (mounted) Navigator.pop(context);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('เกิดข้อผิดพลาด: $e'), backgroundColor: AppTheme.errorColor),
        );
      }
    } finally {
      if (mounted) setState(() => _isSaving = false);
    }
  }
}
