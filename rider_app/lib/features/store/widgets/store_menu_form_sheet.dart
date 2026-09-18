import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/app_theme.dart';
import '../../../models/shop.dart';
import '../providers/store_providers.dart';
import 'store_category_dialog.dart';
import 'store_menu_image_picker.dart';

class StoreMenuFormSheet extends ConsumerStatefulWidget {
  final String shopId;
  final MenuItemDto? existingItem;
  final double? shopLat;
  final double? shopLng;
  final Future<void> Function(Map<String, dynamic> data) onSave;

  const StoreMenuFormSheet({
    super.key,
    required this.shopId,
    this.existingItem,
    this.shopLat,
    this.shopLng,
    required this.onSave,
  });

  @override
  ConsumerState<StoreMenuFormSheet> createState() => _StoreMenuFormSheetState();
}

class _StoreMenuFormSheetState extends ConsumerState<StoreMenuFormSheet> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _nameController;
  late final TextEditingController _priceController;
  late final TextEditingController _descriptionController;
  late final TextEditingController _imageUrlController;
  late final TextEditingController _optionNameController;

  bool _isSaving = false;
  String? _selectedCategoryId;

  @override
  void initState() {
    super.initState();
    final item = widget.existingItem;
    _nameController = TextEditingController(text: item?.name ?? '');
    _priceController = TextEditingController(text: item != null ? item.price.toStringAsFixed(0) : '');
    _descriptionController = TextEditingController(text: item?.description ?? '');
    _imageUrlController = TextEditingController(text: item?.imageUrl ?? '');
    _optionNameController = TextEditingController();
    _selectedCategoryId = item?.menuCategoryId;
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

  Future<void> _handleAddCategory() async {
    final result = await showAddCategoryDialog(context, ref);
    if (result != null && mounted) {
      setState(() {
        _selectedCategoryId = result.id;
      });
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('สร้างหมวดหมู่ "${result.name}" สำเร็จ')),
      );
    }
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
                  StoreMenuImagePicker(controller: _imageUrlController),
                  const SizedBox(height: 16),
                  ref.watch(menuCategoriesProvider).maybeWhen(
                    data: (categories) {
                      final dropdownValue = (_selectedCategoryId != null &&
                              categories.any((cat) => cat.id == _selectedCategoryId))
                          ? _selectedCategoryId
                          : null;
                      return Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            crossAxisAlignment: CrossAxisAlignment.end,
                            children: [
                              Expanded(
                                child: DropdownButtonFormField<String?>(
                                  value: dropdownValue,
                                  dropdownColor: AppTheme.surfaceCard,
                                  decoration: const InputDecoration(
                                    labelText: 'หมวดหมู่สินค้า (ไม่บังคับ)',
                                    prefixIcon: Icon(Icons.category_outlined),
                                  ),
                                  items: [
                                    const DropdownMenuItem<String?>(
                                      value: null,
                                      child: Text('ไม่มีหมวดหมู่'),
                                    ),
                                    ...categories.map((cat) => DropdownMenuItem<String?>(
                                          value: cat.id,
                                          child: Text(cat.name),
                                        )),
                                  ],
                                  onChanged: (val) {
                                    setState(() {
                                      _selectedCategoryId = val;
                                    });
                                  },
                                ),
                              ),
                              const SizedBox(width: 8),
                              SizedBox(
                                height: 52,
                                width: 52,
                                child: IconButton(
                                  onPressed: _handleAddCategory,
                                  icon: const Icon(Icons.add),
                                  style: IconButton.styleFrom(
                                    backgroundColor: AppTheme.primaryColor.withOpacity(0.1),
                                    foregroundColor: AppTheme.primaryColor,
                                    shape: RoundedRectangleBorder(
                                      borderRadius: BorderRadius.circular(12),
                                    ),
                                  ),
                                  tooltip: 'เพิ่มหมวดหมู่ใหม่',
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: 16),
                        ],
                      );
                    },
                    orElse: () => const SizedBox.shrink(),
                  ),
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
                  TextFormField(
                    controller: _descriptionController,
                    maxLines: 3,
                    decoration: const InputDecoration(
                      labelText: 'รายละเอียดเมนู (ไม่บังคับ)',
                      prefixIcon: Icon(Icons.description_outlined),
                    ),
                  ),
                  const SizedBox(height: 16),
                  TextFormField(
                    controller: _optionNameController,
                    decoration: const InputDecoration(
                      labelText: 'ออฟชั่นเสริม (ไม่บังคับ เช่น ไซส์, ท็อปปิ้ง)',
                      prefixIcon: Icon(Icons.add_circle_outline),
                    ),
                  ),
                  const SizedBox(height: 24),
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
      };

      if (widget.existingItem == null) {
        data['ShopId'] = widget.shopId;
      }
      if (_selectedCategoryId != null) {
        data['MenuCategoryId'] = _selectedCategoryId;
      } else if (widget.existingItem?.menuCategoryId != null) {
        data['MenuCategoryId'] = '';
      }

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
