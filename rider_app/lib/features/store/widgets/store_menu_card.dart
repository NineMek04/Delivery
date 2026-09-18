import 'dart:convert';
import 'package:flutter/material.dart';
import '../../../app/app_theme.dart';
import '../../../models/shop.dart';

enum StoreMenuMode { view, delete, edit }

class StoreMenuCard extends StatelessWidget {
  final MenuItemDto item;
  final StoreMenuMode mode;
  final bool isSelectedForDelete;
  final bool isSelectedForEdit;
  final VoidCallback onDeleteToggle;
  final VoidCallback onEditSelect;

  const StoreMenuCard({
    super.key,
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
      onTap: mode == StoreMenuMode.delete
          ? onDeleteToggle
          : mode == StoreMenuMode.edit
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
                Expanded(
                  flex: 3,
                  child: Container(
                    width: double.infinity,
                    color: AppTheme.surfaceElevated,
                    child: _buildImage(item.imageUrl),
                  ),
                ),
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
            if (mode == StoreMenuMode.delete)
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
            if (mode == StoreMenuMode.edit)
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

class StoreActionChip extends StatelessWidget {
  final IconData icon;
  final String label;
  final Color color;
  final VoidCallback onTap;
  final bool isActive;

  const StoreActionChip({
    super.key,
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
