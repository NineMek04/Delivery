import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';
import '../../../../models/shop.dart';
import '../../../../app/app_theme.dart';
import '../../cart/providers/cart_provider.dart';

class DishOptionsBottomSheet extends ConsumerStatefulWidget {
  final MenuItemDto dish;
  final bool isBuyNow;

  const DishOptionsBottomSheet({
    super.key,
    required this.dish,
    this.isBuyNow = false,
  });

  @override
  ConsumerState<DishOptionsBottomSheet> createState() => _DishOptionsBottomSheetState();
}

class _DishOptionsBottomSheetState extends ConsumerState<DishOptionsBottomSheet> {
  final Map<String, List<MenuItemOptionItemDto>> _selectedOptions = {};
  int _quantity = 1;
  final _notesController = TextEditingController();

  @override
  void initState() {
    super.initState();
    // Initialize empty selection lists for each option group
    if (widget.dish.options != null) {
      for (final opt in widget.dish.options!) {
        _selectedOptions[opt.name] = [];
      }
    }
  }

  @override
  void dispose() {
    _notesController.dispose();
    super.dispose();
  }

  double get _optionsPriceTotal {
    double total = 0.0;
    _selectedOptions.forEach((_, items) {
      for (final item in items) {
        total += item.price;
      }
    });
    return total;
  }

  double get _totalPrice {
    return (widget.dish.price + _optionsPriceTotal) * _quantity;
  }

  void _toggleSelection(MenuItemOptionDto option, MenuItemOptionItemDto item, bool isSelected) {
    setState(() {
      final currentList = _selectedOptions[option.name] ?? [];
      if (option.maxSelections == 1) {
        // Radio mode: select only this item
        if (isSelected) {
          _selectedOptions[option.name] = [item];
        } else {
          _selectedOptions[option.name] = [];
        }
      } else {
        // Checkbox mode: toggle item
        if (isSelected) {
          if (currentList.length < option.maxSelections) {
            currentList.add(item);
          } else {
            // Show alert if limit is exceeded
            ScaffoldMessenger.of(context).showSnackBar(
              SnackBar(
                content: Text('คุณสามารถเลือกตัวเลือก "${option.name}" ได้สูงสุด ${option.maxSelections} อย่าง'),
                backgroundColor: Colors.orange,
                duration: const Duration(seconds: 2),
              ),
            );
          }
        } else {
          currentList.removeWhere((i) => i.name == item.name);
        }
        _selectedOptions[option.name] = currentList;
      }
    });
  }

  bool _isItemSelected(String optionName, String itemName) {
    final list = _selectedOptions[optionName] ?? [];
    return list.any((i) => i.name == itemName);
  }

  void _submit() {
    // Validate required options
    if (widget.dish.options != null) {
      for (final opt in widget.dish.options!) {
        final list = _selectedOptions[opt.name] ?? [];
        if (opt.required && list.isEmpty) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Text('กรุณาเลือกตัวเลือกที่จำเป็น: ${opt.name}'),
              backgroundColor: Colors.red,
            ),
          );
          return;
        }
      }
    }

    // Build options description string
    final List<String> descriptions = [];
    _selectedOptions.forEach((optName, items) {
      if (items.isNotEmpty) {
        final itemsStr = items.map((i) => i.price > 0 ? '${i.name} (+฿${i.price.toInt()})' : i.name).join(', ');
        descriptions.add('$optName: $itemsStr');
      }
    });

    final optionsDescription = descriptions.join(' | ');
    final optionsPrice = _optionsPriceTotal;

    if (widget.isBuyNow) {
      ref.read(cartProvider.notifier).clearCart();
    }

    // Add to cart with riverpod provider
    for (int i = 0; i < _quantity; i++) {
      ref.read(cartProvider.notifier).addItem(
        widget.dish,
        optionsDescription: optionsDescription,
        optionsPrice: optionsPrice,
        notes: _notesController.text.trim().isNotEmpty ? _notesController.text.trim() : null,
      );
    }

    if (widget.isBuyNow) {
      Navigator.pop(context, true);
    } else {
      Navigator.pop(context);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('เพิ่ม ${widget.dish.name} ลงตะกร้าแล้ว'),
          backgroundColor: Colors.green,
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final formatCurrency = NumberFormat.currency(locale: 'th', symbol: '฿', decimalDigits: 0);

    return Container(
      decoration: BoxDecoration(
        color: isDark ? const Color(0xFF1E1E2E) : Colors.white,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(24)),
      ),
      padding: EdgeInsets.only(
        bottom: MediaQuery.of(context).viewInsets.bottom + 24,
        left: 20,
        right: 20,
        top: 16,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          // Pull bar
          Center(
            child: Container(
              width: 40,
              height: 4,
              decoration: BoxDecoration(
                color: Colors.grey[300],
                borderRadius: BorderRadius.circular(2),
              ),
            ),
          ),
          const SizedBox(height: 16),

          // Dish Info
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      widget.dish.name,
                      style: const TextStyle(
                        fontSize: 20,
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                    if (widget.dish.description != null && widget.dish.description!.isNotEmpty) ...[
                      const SizedBox(height: 6),
                      Text(
                        widget.dish.description!,
                        style: TextStyle(fontSize: 14, color: Colors.grey[600]),
                      ),
                    ],
                    const SizedBox(height: 8),
                    Text(
                      formatCurrency.format(widget.dish.price),
                      style: const TextStyle(
                        fontSize: 18,
                        fontWeight: FontWeight.bold,
                        color: AppTheme.primaryColor,
                      ),
                    ),
                  ],
                ),
              ),
              if (widget.dish.imageUrl != null && widget.dish.imageUrl!.isNotEmpty) ...[
                const SizedBox(width: 16),
                Container(
                  width: 80,
                  height: 80,
                  decoration: BoxDecoration(
                    borderRadius: BorderRadius.circular(12),
                    image: DecorationImage(
                      image: NetworkImage(widget.dish.imageUrl!),
                      fit: BoxFit.cover,
                    ),
                  ),
                ),
              ],
            ],
          ),
          const Divider(height: 32),

          // Scrollable Option Categories List
          ConstrainedBox(
            constraints: BoxConstraints(
              maxHeight: MediaQuery.of(context).size.height * 0.4,
            ),
            child: SingleChildScrollView(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  if (widget.dish.options != null)
                    ...widget.dish.options!.map((opt) {
                      return Padding(
                        padding: const EdgeInsets.only(bottom: 24),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Row(
                              children: [
                                Text(
                                  opt.name,
                                  style: const TextStyle(
                                    fontSize: 16,
                                    fontWeight: FontWeight.bold,
                                  ),
                                ),
                                if (opt.required) ...[
                                  const SizedBox(width: 4),
                                  const Text(
                                    '* (จำเป็น)',
                                    style: TextStyle(
                                      color: Colors.red,
                                      fontSize: 12,
                                      fontWeight: FontWeight.bold,
                                    ),
                                  ),
                                ],
                              ],
                            ),
                            Text(
                              'เลือกสูงสุดได้ ${opt.maxSelections} อย่าง',
                              style: TextStyle(fontSize: 12, color: Colors.grey[600]),
                            ),
                            const SizedBox(height: 8),
                            if (opt.items != null)
                              ...opt.items!.map((choice) {
                                final isSelected = _isItemSelected(opt.name, choice.name);
                                return Container(
                                  margin: const EdgeInsets.only(bottom: 4),
                                  decoration: BoxDecoration(
                                    border: Border.all(
                                      color: isSelected
                                          ? AppTheme.primaryColor.withOpacity(0.5)
                                          : Colors.grey.shade200,
                                    ),
                                    borderRadius: BorderRadius.circular(12),
                                    color: isSelected
                                        ? AppTheme.primaryColor.withOpacity(0.04)
                                        : Colors.transparent,
                                  ),
                                  child: CheckboxListTile(
                                    value: isSelected,
                                    activeColor: AppTheme.primaryColor,
                                    title: Text(choice.name, style: const TextStyle(fontSize: 14)),
                                    subtitle: choice.price > 0
                                        ? Text('+${formatCurrency.format(choice.price)}',
                                            style: const TextStyle(color: Colors.grey))
                                        : null,
                                    onChanged: (val) {
                                      _toggleSelection(opt, choice, val ?? false);
                                    },
                                    controlAffinity: ListTileControlAffinity.trailing,
                                  ),
                                );
                              }),
                          ],
                        ),
                      );
                    }),

                  // Notes Text Field
                  const Text(
                    'รายละเอียดเพิ่มเติมถึงเชฟ (ถ้ามี)',
                    style: TextStyle(
                      fontSize: 16,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                  const SizedBox(height: 8),
                  TextField(
                    controller: _notesController,
                    maxLines: 2,
                    decoration: InputDecoration(
                      hintText: 'ตัวอย่าง: ขอเผ็ดน้อย, ไม่ใส่ผัก, แยกน้ำซอส...',
                      border: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(12),
                        borderSide: BorderSide(color: Colors.grey.shade300),
                      ),
                      focusedBorder: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(12),
                        borderSide: const BorderSide(color: AppTheme.primaryColor),
                      ),
                    ),
                  ),
                  const SizedBox(height: 16),
                ],
              ),
            ),
          ),
          const Divider(height: 32),

          // Bottom Action Panel
          Row(
            children: [
              // Quantity adjust
              Container(
                decoration: BoxDecoration(
                  border: Border.all(color: Colors.grey.shade300),
                  borderRadius: BorderRadius.circular(28),
                ),
                child: Row(
                  children: [
                    IconButton(
                      icon: const Icon(Icons.remove),
                      onPressed: _quantity > 1 ? () => setState(() => _quantity--) : null,
                    ),
                    Text(
                      '$_quantity',
                      style: const TextStyle(fontSize: 16, fontWeight: FontWeight.bold),
                    ),
                    IconButton(
                      icon: const Icon(Icons.add),
                      onPressed: () => setState(() => _quantity++),
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 16),
              // Add to cart button
              Expanded(
                child: ElevatedButton(
                  onPressed: _submit,
                  style: ElevatedButton.styleFrom(
                    padding: const EdgeInsets.symmetric(vertical: 16),
                    backgroundColor: AppTheme.primaryColor,
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(28),
                    ),
                  ),
                  child: Text(
                    widget.isBuyNow
                        ? 'ซื้อทันที • ${formatCurrency.format(_totalPrice)}'
                        : 'เพิ่มลงตะกร้า • ${formatCurrency.format(_totalPrice)}',
                    style: const TextStyle(
                      fontSize: 16,
                      fontWeight: FontWeight.bold,
                      color: Colors.white,
                    ),
                  ),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
