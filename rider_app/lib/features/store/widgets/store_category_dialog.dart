import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../../app/app_theme.dart';
import '../../../models/shop.dart';
import '../providers/store_providers.dart';

Future<MenuCategoryDto?> showAddCategoryDialog(BuildContext context, WidgetRef ref) async {
  final nameController = TextEditingController();
  final descController = TextEditingController();
  final dialogFormKey = GlobalKey<FormState>();
  bool isDialogSaving = false;

  return showDialog<MenuCategoryDto?>(
    context: context,
    builder: (ctx) {
      return StatefulBuilder(
        builder: (context, setStateDialog) {
          return AlertDialog(
            backgroundColor: AppTheme.surfaceCard,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(20),
            ),
            title: const Text(
              'สร้างหมวดหมู่ใหม่',
              style: TextStyle(fontWeight: FontWeight.bold),
            ),
            content: Form(
              key: dialogFormKey,
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  TextFormField(
                    controller: nameController,
                    autofocus: true,
                    decoration: const InputDecoration(
                      labelText: 'ชื่อหมวดหมู่ *',
                      hintText: 'เช่น อาหารจานเดียว, เครื่องดื่ม',
                      prefixIcon: Icon(Icons.edit_outlined),
                    ),
                    validator: (v) {
                      if (v == null || v.trim().isEmpty) {
                        return 'กรุณากรอกชื่อหมวดหมู่';
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),
                  TextFormField(
                    controller: descController,
                    decoration: const InputDecoration(
                      labelText: 'คำอธิบาย (ไม่บังคับ)',
                      hintText: 'รายละเอียดสั้นๆ ของหมวดหมู่',
                      prefixIcon: Icon(Icons.description_outlined),
                    ),
                  ),
                ],
              ),
            ),
            actions: [
              TextButton(
                onPressed: isDialogSaving ? null : () => Navigator.pop(ctx),
                child: const Text('ยกเลิก'),
              ),
              ElevatedButton(
                onPressed: isDialogSaving
                    ? null
                    : () async {
                        if (!dialogFormKey.currentState!.validate()) return;
                        setStateDialog(() => isDialogSaving = true);
                        try {
                          final newCat = await ref
                              .read(menuCategoriesProvider.notifier)
                              .addCategory(
                                nameController.text.trim(),
                                description: descController.text.trim().isNotEmpty
                                    ? descController.text.trim()
                                    : null,
                              );
                          if (context.mounted) {
                            Navigator.pop(ctx, newCat);
                          }
                        } catch (e) {
                          if (context.mounted) {
                            ScaffoldMessenger.of(context).showSnackBar(
                              SnackBar(
                                content: Text('สร้างหมวดหมู่ล้มเหลว: $e'),
                                backgroundColor: AppTheme.errorColor,
                              ),
                            );
                          }
                        } finally {
                          setStateDialog(() => isDialogSaving = false);
                        }
                      },
                child: isDialogSaving
                    ? const SizedBox(
                        height: 20,
                        width: 20,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Text('สร้าง'),
              ),
            ],
          );
        },
      );
    },
  );
}
