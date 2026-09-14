import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../../app/app_theme.dart';
import '../../../core/auth/auth_service.dart';
import '../../../core/config/environment.dart';
import '../../../models/store_report.dart';
import '../providers/store_providers.dart';

/// Store Summary Screen — Revenue & Orders analytics, Top items, Detail breakdown, and CSV Export.
class StoreSummaryScreen extends ConsumerWidget {
  const StoreSummaryScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final period = ref.watch(storeReportPeriodProvider);
    final reportAsync = ref.watch(storeReportSummaryProvider);
    final shopAsync = ref.watch(currentShopProvider);

    final currencyFmt = NumberFormat('#,##0');
    final dateFmt = DateFormat('dd/MM/yyyy HH:mm');

    return Scaffold(
      appBar: AppBar(
        title: const Text('สรุปยอดขายและบัญชี'),
        actions: [
          IconButton(
            tooltip: 'รีเฟรชข้อมูล',
            icon: const Icon(Icons.refresh),
            onPressed: () => ref.invalidate(storeReportSummaryProvider),
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          ref.invalidate(storeReportSummaryProvider);
        },
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            // ── Period Filter (Day / Month / Year) ───────────────────
            Center(
              child: SegmentedButton<String>(
                segments: const [
                  ButtonSegment(value: 'day', label: Text('วันนี้')),
                  ButtonSegment(value: 'month', label: Text('เดือนนี้')),
                  ButtonSegment(value: 'year', label: Text('ทั้งปี')),
                ],
                selected: {period},
                onSelectionChanged: (Set<String> selected) {
                  ref.read(storeReportPeriodProvider.notifier).state =
                      selected.first;
                },
                style: SegmentedButton.styleFrom(
                  selectedBackgroundColor: AppTheme.primaryColor,
                  selectedForegroundColor: Colors.white,
                ),
              ),
            ),
            const SizedBox(height: 16),

            // ── Data Content ─────────────────────────────────────────
            reportAsync.when(
              data: (report) {
                final summary = report ??
                    const StoreReportSummaryDto(
                      period: 'day',
                    );

                return Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    // ── Summary Cards ──────────────────────────────
                    Row(
                      children: [
                        Expanded(
                          child: _StatCard(
                            icon: Icons.attach_money,
                            label: 'ยอดขายรวม',
                            value: '฿${currencyFmt.format(summary.totalRevenue)}',
                            color: AppTheme.accentColor,
                          ),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: _StatCard(
                            icon: Icons.check_circle_outline,
                            label: 'ออเดอร์สำเร็จ',
                            value:
                                '${summary.completedOrders} / ${summary.totalOrders}',
                            color: AppTheme.primaryColor,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 12),
                    Row(
                      children: [
                        Expanded(
                          child: _StatCard(
                            icon: Icons.receipt_long,
                            label: 'เฉลี่ยต่อบิล',
                            value:
                                '฿${currencyFmt.format(summary.averageOrderValue)}',
                            color: const Color(0xFFF59E0B),
                          ),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: _StatCard(
                            icon: Icons.cancel_outlined,
                            label: 'ยกเลิก',
                            value: '${summary.cancelledOrders}',
                            color: summary.cancelledOrders > 0
                                ? AppTheme.errorColor
                                : AppTheme.textSecondary,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 20),

                    // ── Export CSV Action ────────────────────────────
                    shopAsync.when(
                      data: (shop) {
                        if (shop == null) return const SizedBox.shrink();
                        return SizedBox(
                          width: double.infinity,
                          child: OutlinedButton.icon(
                            style: OutlinedButton.styleFrom(
                              foregroundColor: AppTheme.primaryColor,
                              side: const BorderSide(color: AppTheme.primaryColor),
                              padding: const EdgeInsets.symmetric(vertical: 12),
                              shape: RoundedRectangleBorder(
                                borderRadius: BorderRadius.circular(12),
                              ),
                            ),
                            icon: const Icon(Icons.download),
                            label: const Text(
                              'ส่งออกเอกสารรายงาน (Export CSV)',
                              style: TextStyle(fontWeight: FontWeight.w700),
                            ),
                            onPressed: () async {
                              final authService = ref.read(authServiceProvider.notifier);
                              final token = await authService.getAccessToken();
                              final tokenQuery = token != null && token.isNotEmpty ? '&access_token=$token' : '';
                              final url = Uri.parse(
                                '${Environment.apiUrl}/shops/${shop.id}/reports/export?period=$period&format=csv$tokenQuery',
                              );
                              try {
                                if (await canLaunchUrl(url)) {
                                  await launchUrl(url, mode: LaunchMode.externalApplication);
                                } else {
                                  if (context.mounted) {
                                    ScaffoldMessenger.of(context).showSnackBar(
                                      SnackBar(
                                        content: Text('ดาวน์โหลด: $url'),
                                      ),
                                    );
                                  }
                                }
                              } catch (e) {
                                if (context.mounted) {
                                  ScaffoldMessenger.of(context).showSnackBar(
                                    SnackBar(content: Text('เกิดข้อผิดพลาด: $e')),
                                  );
                                }
                              }
                            },
                          ),
                        );
                      },
                      loading: () => const SizedBox.shrink(),
                      error: (_, __) => const SizedBox.shrink(),
                    ),
                    const SizedBox(height: 24),

                    // ── Top Menu Items ──────────────────────────────
                    const _SectionTitle(title: 'เมนูยอดนิยม'),
                    const SizedBox(height: 12),
                    if (summary.topItems.isEmpty)
                      const _EmptyCard(text: 'ยังไม่มีสถิติเมนูสินค้าในช่วงเวลานี้')
                    else
                      ...summary.topItems.asMap().entries.map((entry) {
                        final rank = entry.key + 1;
                        final item = entry.value;
                        return _TopItemTile(
                          rank: rank,
                          name: item.name,
                          quantity: item.quantity,
                          revenue: item.revenue,
                        );
                      }),
                    const SizedBox(height: 24),

                    // ── Detailed Orders List ─────────────────────────
                    Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        const _SectionTitle(title: 'รายละเอียดคำสั่งซื้อ'),
                        Text(
                          '${summary.orders.length} รายการ',
                          style: TextStyle(
                            color: AppTheme.textSecondary,
                            fontSize: 13,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 12),
                    if (summary.orders.isEmpty)
                      const _EmptyCard(text: 'ไม่มีรายการคำสั่งซื้อในช่วงเวลานี้')
                    else
                      ...summary.orders.map((ord) => _OrderDetailCard(
                            order: ord,
                            dateFmt: dateFmt,
                            currencyFmt: currencyFmt,
                          )),
                    const SizedBox(height: 32),
                  ],
                );
              },
              loading: () => const Padding(
                padding: EdgeInsets.symmetric(vertical: 48),
                child: Center(child: CircularProgressIndicator()),
              ),
              error: (err, _) => Padding(
                padding: const EdgeInsets.symmetric(vertical: 48),
                child: Center(
                  child: Column(
                    children: [
                      const Icon(Icons.error_outline,
                          size: 40, color: AppTheme.errorColor),
                      const SizedBox(height: 12),
                      Text('ไม่สามารถโหลดข้อมูลรายงานได้: $err'),
                    ],
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

// ═══════════════════════════════════════════════════════════════════
// Section Title
// ═══════════════════════════════════════════════════════════════════
class _SectionTitle extends StatelessWidget {
  final String title;
  const _SectionTitle({required this.title});

  @override
  Widget build(BuildContext context) {
    return Text(
      title,
      style: Theme.of(context).textTheme.titleMedium?.copyWith(
            fontWeight: FontWeight.w700,
          ),
    );
  }
}

// ═══════════════════════════════════════════════════════════════════
// Empty State Card
// ═══════════════════════════════════════════════════════════════════
class _EmptyCard extends StatelessWidget {
  final String text;
  const _EmptyCard({required this.text});

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(24),
      decoration: BoxDecoration(
        color: AppTheme.surfaceCard,
        borderRadius: BorderRadius.circular(16),
      ),
      child: Center(
        child: Text(
          text,
          style: TextStyle(color: AppTheme.textSecondary, fontSize: 14),
        ),
      ),
    );
  }
}

// ═══════════════════════════════════════════════════════════════════
// Stat Card
// ═══════════════════════════════════════════════════════════════════
class _StatCard extends StatelessWidget {
  final IconData icon;
  final String label;
  final String value;
  final Color color;

  const _StatCard({
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
            style: TextStyle(color: AppTheme.textSecondary, fontSize: 13),
          ),
        ],
      ),
    );
  }
}

// ═══════════════════════════════════════════════════════════════════
// Top Item Tile
// ═══════════════════════════════════════════════════════════════════
class _TopItemTile extends StatelessWidget {
  final int rank;
  final String name;
  final int quantity;
  final double revenue;

  const _TopItemTile({
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
          style: TextStyle(
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
class _OrderDetailCard extends StatelessWidget {
  final StoreOrderDetailDto order;
  final DateFormat dateFmt;
  final NumberFormat currencyFmt;

  const _OrderDetailCard({
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
                style: TextStyle(
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
                Icon(Icons.motorcycle, size: 14, color: AppTheme.textSecondary),
                const SizedBox(width: 4),
                Text(
                  'ไรเดอร์: ${order.riderName}',
                  style: TextStyle(
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
