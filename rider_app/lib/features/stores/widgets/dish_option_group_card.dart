import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../../../../models/shop.dart';
import '../../../../app/app_theme.dart';

class DishOptionGroupCard extends StatelessWidget {
  final MenuItemOptionDto option;
  final bool Function(String groupName, String itemName) isItemSelected;
  final void Function(MenuItemOptionDto option, MenuItemOptionItemDto item, bool isSelected) onToggle;
  final NumberFormat formatCurrency;

  const DishOptionGroupCard({
    super.key,
    required this.option,
    required this.isItemSelected,
    required this.onToggle,
    required this.formatCurrency,
  });

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text(
                option.name,
                style: const TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.bold,
                ),
              ),
              if (option.required) ...[
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
            'เลือกสูงสุดได้ ${option.maxSelections} อย่าง',
            style: TextStyle(fontSize: 12, color: Colors.grey[600]),
          ),
          const SizedBox(height: 8),
          if (option.items != null)
            ...option.items!.map((choice) {
              final isSelected = isItemSelected(option.name, choice.name);
              return Container(
                margin: const EdgeInsets.only(bottom: 4),
                decoration: BoxDecoration(
                  border: Border.all(
                    color: isSelected
                        ? AppTheme.primaryColor.withValues(alpha: 0.5)
                        : Colors.grey.shade200,
                  ),
                  borderRadius: BorderRadius.circular(12),
                  color: isSelected
                      ? AppTheme.primaryColor.withValues(alpha: 0.04)
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
                    onToggle(option, choice, val ?? false);
                  },
                  controlAffinity: ListTileControlAffinity.trailing,
                ),
              );
            }),
        ],
      ),
    );
  }
}
