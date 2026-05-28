import 'dart:math';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/app_theme.dart';
import '../../../models/shop.dart';
import '../providers/store_providers.dart';

/// Store Summary Screen — Page 2: Sales stats, top items, reviews.
///
/// Currently uses mock data (sales/reviews) since these are not yet
/// implemented in the backend. Menu items come from real data.
class StoreSummaryScreen extends ConsumerWidget {
  const StoreSummaryScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final menuAsync = ref.watch(menuItemsProvider);
    final shopAsync = ref.watch(currentShopProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('สรุปยอดขาย')),
      body: RefreshIndicator(
        onRefresh: () async => ref.read(menuItemsProvider.notifier).refresh(),
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            // ── Summary Stats Cards ──────────────────────────────────
            _SectionTitle(title: 'ภาพรวมวันนี้'),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: _StatCard(
                    icon: Icons.shopping_bag,
                    label: 'ออเดอร์วันนี้',
                    value: '12',
                    color: AppTheme.primaryColor,
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: _StatCard(
                    icon: Icons.attach_money,
                    label: 'ยอดขาย',
                    value: '฿2,480',
                    color: AppTheme.accentColor,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: _StatCard(
                    icon: Icons.star,
                    label: 'คะแนนเฉลี่ย',
                    value: '4.7',
                    color: const Color(0xFFF59E0B),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: _StatCard(
                    icon: Icons.people,
                    label: 'ลูกค้าใหม่',
                    value: '5',
                    color: const Color(0xFF8B5CF6),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 24),

            // ── Top Menu Items ──────────────────────────────────────
            _SectionTitle(title: 'เมนูยอดนิยม'),
            const SizedBox(height: 12),
            menuAsync.when(
              data: (items) {
                if (items.isEmpty) {
                  return const Padding(
                    padding: EdgeInsets.all(24),
                    child: Center(child: Text('ยังไม่มีเมนูสินค้า')),
                  );
                }
                // Show top 5 items (mock ordering by name for now)
                final topItems = items.take(5).toList();
                return Column(
                  children: topItems.asMap().entries.map((entry) {
                    return _TopItemTile(
                      rank: entry.key + 1,
                      item: entry.value,
                      orderCount: _mockOrderCount(entry.key),
                    );
                  }).toList(),
                );
              },
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (_, __) => const Center(child: Text('ไม่สามารถโหลดข้อมูลได้')),
            ),
            const SizedBox(height: 24),

            // ── Recent Reviews ──────────────────────────────────────
            _SectionTitle(title: 'รีวิวล่าสุด'),
            const SizedBox(height: 12),
            ..._mockReviews.map((r) => _ReviewCard(review: r)),
            const SizedBox(height: 24),

            // ── Weekly Chart (placeholder) ──────────────────────────
            _SectionTitle(title: 'ยอดขาย 7 วันย้อนหลัง'),
            const SizedBox(height: 12),
            _WeeklyChart(),
            const SizedBox(height: 32),
          ],
        ),
      ),
    );
  }

  int _mockOrderCount(int index) {
    return [45, 38, 27, 19, 12][index % 5];
  }
}

// ── Mock Reviews ─────────────────────────────────────────────────
class _MockReview {
  final String name;
  final double rating;
  final String comment;
  final String time;

  const _MockReview(this.name, this.rating, this.comment, this.time);
}

const _mockReviews = [
  _MockReview('สมชาย', 5.0, 'อร่อยมากครับ จัดส่งเร็ว 👍', '15 นาทีที่แล้ว'),
  _MockReview('สมหญิง', 4.0, 'รสชาติดี แต่รอนานหน่อย', '1 ชั่วโมงที่แล้ว'),
  _MockReview('วิชัย', 5.0, 'ประทับใจเลยครับ สั่งซ้ำแน่นอน!', '3 ชั่วโมงที่แล้ว'),
];

// ═══════════════════════════════════════════════════════════════════
// Section Title
// ═══════════════════════════════════════════════════════════════════
class _SectionTitle extends StatelessWidget {
  final String title;
  const _SectionTitle({required this.title});

  @override
  Widget build(BuildContext context) {
    return Text(title, style: Theme.of(context).textTheme.headlineSmall);
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
            style: Theme.of(context).textTheme.headlineMedium?.copyWith(
                  fontWeight: FontWeight.w800,
                ),
          ),
          const SizedBox(height: 2),
          Text(label, style: Theme.of(context).textTheme.bodyMedium),
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
  final MenuItemDto item;
  final int orderCount;

  const _TopItemTile({
    required this.rank,
    required this.item,
    required this.orderCount,
  });

  @override
  Widget build(BuildContext context) {
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
        title: Text(item.name, maxLines: 1, overflow: TextOverflow.ellipsis),
        subtitle: Text('฿${item.price.toStringAsFixed(0)}'),
        trailing: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            Text(
              '$orderCount',
              style: Theme.of(context).textTheme.titleMedium?.copyWith(
                    fontWeight: FontWeight.w700,
                    color: AppTheme.accentColor,
                  ),
            ),
            const Text('ออเดอร์', style: TextStyle(fontSize: 11)),
          ],
        ),
      ),
    );
  }
}

// ═══════════════════════════════════════════════════════════════════
// Review Card
// ═══════════════════════════════════════════════════════════════════
class _ReviewCard extends StatelessWidget {
  final _MockReview review;

  const _ReviewCard({required this.review});

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: AppTheme.surfaceCard,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              CircleAvatar(
                radius: 16,
                backgroundColor: AppTheme.primaryColor.withValues(alpha: 0.15),
                child: Text(
                  review.name[0],
                  style: const TextStyle(
                    color: AppTheme.primaryColor,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(review.name, style: Theme.of(context).textTheme.titleSmall),
                    Text(review.time, style: const TextStyle(fontSize: 11, color: AppTheme.textMuted)),
                  ],
                ),
              ),
              ...List.generate(
                5,
                (i) => Icon(
                  i < review.rating ? Icons.star : Icons.star_border,
                  size: 16,
                  color: const Color(0xFFF59E0B),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(review.comment, style: Theme.of(context).textTheme.bodyMedium),
        ],
      ),
    );
  }
}

// ═══════════════════════════════════════════════════════════════════
// Weekly Chart (Simple bar chart via CustomPaint)
// ═══════════════════════════════════════════════════════════════════
class _WeeklyChart extends StatelessWidget {
  final List<double> data = const [1200, 1800, 1500, 2200, 1900, 2480, 1600];
  final List<String> labels = const ['จ', 'อ', 'พ', 'พฤ', 'ศ', 'ส', 'อา'];

  @override
  Widget build(BuildContext context) {
    final maxVal = data.reduce((a, b) => a > b ? a : b);

    return Container(
      height: 200,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppTheme.surfaceCard,
        borderRadius: BorderRadius.circular(16),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: List.generate(data.length, (i) {
          final height = (data[i] / maxVal) * 140;
          return Expanded(
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 4),
              child: Column(
                mainAxisAlignment: MainAxisAlignment.end,
                children: [
                  Text(
                    '${(data[i] / 1000).toStringAsFixed(1)}k',
                    style: const TextStyle(fontSize: 10, color: AppTheme.textMuted),
                  ),
                  const SizedBox(height: 4),
                  AnimatedContainer(
                    duration: const Duration(milliseconds: 500),
                    height: height,
                    decoration: BoxDecoration(
                      gradient: LinearGradient(
                        begin: Alignment.bottomCenter,
                        end: Alignment.topCenter,
                        colors: [
                          AppTheme.primaryColor.withValues(alpha: 0.6),
                          AppTheme.primaryColor,
                        ],
                      ),
                      borderRadius: BorderRadius.circular(6),
                    ),
                  ),
                  const SizedBox(height: 6),
                  Text(labels[i], style: const TextStyle(fontSize: 12)),
                ],
              ),
            ),
          );
        }),
      ),
    );
  }
}
