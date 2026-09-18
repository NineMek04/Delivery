import '../widgets/store_summary_widgets.dart';
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
                          child: StoreSummaryStatCard(
                            icon: Icons.attach_money,
                            label: 'ยอดขายรวม',
                            value: '฿${currencyFmt.format(summary.totalRevenue)}',
                            color: AppTheme.accentColor,
                          ),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: StoreSummaryStatCard(
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
                          child: StoreSummaryStatCard(
                            icon: Icons.receipt_long,
                            label: 'เฉลี่ยต่อบิล',
                            value:
                                '฿${currencyFmt.format(summary.averageOrderValue)}',
                            color: const Color(0xFFF59E0B),
                          ),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: StoreSummaryStatCard(
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
                              final token = authService.currentToken;
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
                        return StoreSummaryTopItemTile(
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
                      ...summary.orders.map((ord) => StoreSummaryOrderDetailCard(
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
