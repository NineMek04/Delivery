import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../../../app/app_theme.dart';
import '../../../models/store_report.dart';

// ═══════════════════════════════════════════════════════════════════
// Stat Card
// ═══════════════════════════════════════════════════════════════════
class StoreSummaryStatCard extends StatelessWidget {
  final IconData icon;
  final String label;
  final String value;
  final Color color;

  const StoreSummaryStatCard({
    super.key,
    required this.icon,
    required this.label,
    required this.value,
    required this.color,
  });

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppTheme.surfaceCard,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: color.withValues(alpha: 0.2)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            padding: const EdgeInsets.all(8),
            decoration: BoxDecoration(
              color: color.withValues(alpha: 0.15),
              borderRadius: BorderRadius.circular(10),
            ),
            child: Icon(icon, color: color, size: 22),
          ),
          const SizedBox(height: 12),
          Text(
            value,
            style: Theme.of(context).textTheme.titleLarge?.copyWith(
                  fontWeight: FontWeight.w800,
                ),
          ),
          const SizedBox(height: 2),
          Text(
            label,
            style: const TextStyle(color: AppTheme.textSecondary, fontSize: 13),
          ),
        ],
      ),
    );
  }
}

// ═══════════════════════════════════════════════════════════════════
// Top Item Tile
// ═══════════════════════════════════════════════════════════════════
class StoreSummaryTopItemTile extends StatelessWidget {
  final int rank;
  final String name;
  final int quantity;
  final double revenue;

  const StoreSummaryTopItemTile({
    super.key,
    required this.rank,
    required this.name,
    required this.quantity,
    required this.revenue,
  });

  @override
  Widget build(BuildContext context) {
    final currencyFmt = NumberFormat('#,##0');

    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: ListTile(
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
        tileColor: AppTheme.surfaceCard,
        leading: CircleAvatar(
          backgroundColor: rank <= 3
              ? AppTheme.primaryColor.withValues(alpha: 0.2)
              : AppTheme.surfaceElevated,
          child: Text(
            '#$rank',
            style: TextStyle(
              color: rank <= 3 ? AppTheme.primaryColor : AppTheme.textPrimary,
              fontWeight: FontWeight.w700,
            ),
          ),
        ),
        title: Text(name, maxLines: 1, overflow: TextOverflow.ellipsis),
        subtitle: Text('ขายได้ $quantity จาน'),
        trailing: Text(
          '฿${currencyFmt.format(revenue)}',
          style: const TextStyle(
            fontWeight: FontWeight.w700,
            color: AppTheme.accentColor,
          ),
        ),
      ),
    );
  }
}

// ═══════════════════════════════════════════════════════════════════
// Order Detail Card
// ═══════════════════════════════════════════════════════════════════
class StoreSummaryOrderDetailCard extends StatelessWidget {
  final StoreOrderDetailDto order;
  final DateFormat dateFmt;
  final NumberFormat currencyFmt;

  const StoreSummaryOrderDetailCard({
    super.key,
    required this.order,
    required this.dateFmt,
    required this.currencyFmt,
  });

  Color _statusColor(String status) {
    switch (status.toLowerCase()) {
      case 'delivered':
      case 'completed':
        return AppTheme.accentColor;
      case 'cancelled':
        return AppTheme.errorColor;
      default:
        return AppTheme.primaryColor;
    }
  }

  @override
  Widget build(BuildContext context) {
    final statusColor = _statusColor(order.status);

    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: AppTheme.surfaceCard,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: Colors.white.withValues(alpha: 0.05)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                '#${order.trackingNumber}',
                style: const TextStyle(
                  fontWeight: FontWeight.w700,
                  fontSize: 14,
                ),
              ),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                decoration: BoxDecoration(
                  color: statusColor.withValues(alpha: 0.15),
                  borderRadius: BorderRadius.circular(6),
                ),
                child: Text(
                  order.status,
                  style: TextStyle(
                    color: statusColor,
                    fontSize: 11,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                order.createdAt != null
                    ? dateFmt.format(order.createdAt!.toLocal())
                    : '-',
                style: const TextStyle(
                  color: AppTheme.textSecondary,
                  fontSize: 12,
                ),
              ),
              Text(
                '฿${currencyFmt.format(order.totalAmount)}',
                style: const TextStyle(
                  fontWeight: FontWeight.w800,
                  fontSize: 15,
                ),
              ),
            ],
          ),
          if (order.riderName != null && order.riderName!.isNotEmpty) ...[
            const SizedBox(height: 4),
            Row(
              children: [
                const Icon(Icons.motorcycle, size: 14, color: AppTheme.textSecondary),
                const SizedBox(width: 4),
                Text(
                  'ไรเดอร์: ${order.riderName}',
                  style: const TextStyle(
                    color: AppTheme.textSecondary,
                    fontSize: 12,
                  ),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}
