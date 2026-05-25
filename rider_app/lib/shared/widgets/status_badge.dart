import 'package:flutter/material.dart';

import '../../app/app_theme.dart';

/// Badge แสดงสถานะ Order / Rider พร้อมสีตาม state.
class StatusBadge extends StatelessWidget {
  final String status;
  final bool compact;

  const StatusBadge({
    super.key,
    required this.status,
    this.compact = false,
  });

  Color _color() {
    return AppTheme.orderStatusColors[status.toUpperCase()] ??
        AppTheme.riderStatusColors[status.toUpperCase()] ??
        AppTheme.textMuted;
  }

  @override
  Widget build(BuildContext context) {
    final color = _color();
    return Container(
      padding: EdgeInsets.symmetric(
        horizontal: compact ? 8 : 12,
        vertical: compact ? 2 : 4,
      ),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.2),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: color.withValues(alpha: 0.5)),
      ),
      child: Text(
        status.toUpperCase(),
        style: TextStyle(
          color: color,
          fontSize: compact ? 10 : 12,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}
