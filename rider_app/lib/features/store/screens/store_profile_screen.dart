import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import '../../../app/app_theme.dart';
import '../../../core/api/services/shop_api_service.dart';
import '../../../core/auth/auth_service.dart';
import '../../../models/shop.dart';
import '../providers/store_providers.dart';
import '../widgets/edit_shop_form_sheet.dart';

/// Store Profile Screen — Page 3: Shop info, online/offline toggle, logout.
class StoreProfileScreen extends ConsumerStatefulWidget {
  const StoreProfileScreen({super.key});

  @override
  ConsumerState<StoreProfileScreen> createState() => _StoreProfileScreenState();
}

class _StoreProfileScreenState extends ConsumerState<StoreProfileScreen> {
  bool _isSaving = false;

  @override
  Widget build(BuildContext context) {
    final shopAsync = ref.watch(currentShopProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('โปรไฟล์ร้านค้า')),
      body: shopAsync.when(
        data: (shop) {
          if (shop == null) {
            return const Center(child: Text('ไม่พบข้อมูลร้านค้า'));
          }
          return _buildProfileBody(context, shop);
        },
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              const Icon(Icons.error_outline, size: 48, color: AppTheme.errorColor),
              const SizedBox(height: 16),
              Text('เกิดข้อผิดพลาด: $e'),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildProfileBody(BuildContext context, ShopDto shop) {
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        // ── Shop Avatar & Name ──────────────────────────────────
        Center(
          child: Column(
            children: [
              Container(
                width: 100,
                height: 100,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  gradient: LinearGradient(
                    colors: [
                      AppTheme.primaryColor,
                      AppTheme.primaryColor.withValues(alpha: 0.6),
                    ],
                  ),
                ),
                child: const Center(
                  child: Icon(Icons.storefront, size: 48, color: Colors.white),
                ),
              ),
              const SizedBox(height: 16),
              Text(
                shop.name,
                style: Theme.of(context).textTheme.headlineMedium,
              ),
              const SizedBox(height: 4),
              Text(
                'รหัสร้าน: ${shop.trackingCode.isNotEmpty ? shop.trackingCode : shop.id.substring(0, 8)}',
                style: Theme.of(context).textTheme.bodyMedium,
              ),
            ],
          ),
        ),
        const SizedBox(height: 32),

        // ── Online/Offline Toggle ────────────────────────────────
        _ProfileCard(
          child: SwitchListTile.adaptive(
            title: const Text('สถานะร้าน'),
            subtitle: Text(
              shop.isOpen ? 'เปิดร้าน — รับออเดอร์อยู่' : 'ปิดร้าน — หยุดรับออเดอร์',
              style: TextStyle(
                color: shop.isOpen ? AppTheme.accentColor : AppTheme.textMuted,
                fontWeight: FontWeight.w600,
              ),
            ),
            value: shop.isOpen,
            activeColor: AppTheme.accentColor,
            secondary: Icon(
              shop.isOpen ? Icons.circle : Icons.circle_outlined,
              color: shop.isOpen ? AppTheme.accentColor : AppTheme.textMuted,
            ),
            onChanged: _isSaving ? null : (v) => _toggleShopStatus(shop, v),
          ),
        ),
        const SizedBox(height: 12),

        // ── Shop Info ────────────────────────────────────────────
        _ProfileCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Padding(
                padding: EdgeInsets.fromLTRB(16, 16, 16, 8),
                child: Text('ข้อมูลร้านค้า', style: TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
              ),
              _InfoTile(
                icon: Icons.restaurant,
                label: 'ชื่อเมนูหลัก',
                value: shop.menuName.isNotEmpty ? shop.menuName : '—',
              ),
              _InfoTile(
                icon: Icons.attach_money,
                label: 'ราคาเมนูเริ่มต้น',
                value: '฿${shop.menuPrice.toStringAsFixed(0)}',
              ),
              _InfoTile(
                icon: Icons.timer,
                label: 'เวลาเตรียมอาหาร',
                value: '${shop.prepTimeMinutes} นาที',
              ),
              _InfoTile(
                icon: Icons.schedule,
                label: 'เวลาเปิด-ปิด',
                value: shop.openingHours ?? 'ไม่ได้ตั้งค่า',
              ),
              _InfoTile(
                icon: Icons.location_on,
                label: 'พิกัด',
                value: shop.lat != null && shop.lng != null
                    ? '${shop.lat!.toStringAsFixed(4)}, ${shop.lng!.toStringAsFixed(4)}'
                    : 'ยังไม่ได้ตั้งค่า',
              ),
              _InfoTile(
                icon: Icons.calendar_today,
                label: 'วันที่สร้าง',
                value: shop.createdAt != null
                    ? '${shop.createdAt!.day}/${shop.createdAt!.month}/${shop.createdAt!.year}'
                    : '—',
              ),
              const SizedBox(height: 8),
            ],
          ),
        ),
        const SizedBox(height: 12),

        // ── Edit shop info ───────────────────────────────────────
        _ProfileCard(
          child: ListTile(
            leading: const Icon(Icons.edit, color: AppTheme.primaryColor),
            title: const Text('แก้ไขข้อมูลร้าน'),
            trailing: const Icon(Icons.chevron_right),
            onTap: () => _showEditShopDialog(context, shop),
          ),
        ),
        const SizedBox(height: 12),

        // ── Server Settings ──────────────────────────────────────
        _ProfileCard(
          child: ListTile(
            leading: const Icon(Icons.dns_outlined, color: AppTheme.primaryColor),
            title: const Text('ตั้งค่า Server URL'),
            subtitle: const Text('กำหนด Cloudflare Tunnel หรือ IP เซิร์ฟเวอร์', style: TextStyle(fontSize: 12)),
            trailing: const Icon(Icons.chevron_right),
            onTap: () => context.push('/server-settings'),
          ),
        ),
        const SizedBox(height: 24),

        // ── Logout ───────────────────────────────────────────────
        SizedBox(
          width: double.infinity,
          child: OutlinedButton.icon(
            onPressed: () => _confirmLogout(context),
            icon: const Icon(Icons.logout, color: AppTheme.errorColor),
            label: const Text('ออกจากระบบ', style: TextStyle(color: AppTheme.errorColor)),
            style: OutlinedButton.styleFrom(
              side: const BorderSide(color: AppTheme.errorColor),
              padding: const EdgeInsets.symmetric(vertical: 14),
              shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
            ),
          ),
        ),
        const SizedBox(height: 32),
      ],
    );
  }

  Future<void> _toggleShopStatus(ShopDto shop, bool isOpen) async {
    setState(() => _isSaving = true);
    try {
      final shopApi = ref.read(shopApiServiceProvider);
      final data = shop.toJson();
      data['IsOpen'] = isOpen;
      await shopApi.update(shop.id, data);
      ref.invalidate(currentShopProvider);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('ไม่สามารถเปลี่ยนสถานะได้: $e')),
        );
      }
    } finally {
      if (mounted) setState(() => _isSaving = false);
    }
  }

  void _showEditShopDialog(BuildContext context, ShopDto shop) {
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: AppTheme.surfaceCard,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
      ),
      builder: (ctx) => EditShopFormSheet(shop: shop),
    );
  }

  Future<void> _confirmLogout(BuildContext context) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('ออกจากระบบ'),
        content: const Text('ต้องการออกจากระบบหรือไม่?'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('ยกเลิก')),
          TextButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('ออกจากระบบ', style: TextStyle(color: AppTheme.errorColor)),
          ),
        ],
      ),
    );

    if (confirmed == true && mounted) {
      await ref.read(authServiceProvider.notifier).logout();
    }
  }
}

// ═══════════════════════════════════════════════════════════════════
// Profile Card Container
// ═══════════════════════════════════════════════════════════════════
class _ProfileCard extends StatelessWidget {
  final Widget child;
  const _ProfileCard({required this.child});

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: AppTheme.surfaceCard,
        borderRadius: BorderRadius.circular(16),
      ),
      clipBehavior: Clip.antiAlias,
      child: child,
    );
  }
}

// ═══════════════════════════════════════════════════════════════════
// Info Tile
// ═══════════════════════════════════════════════════════════════════
class _InfoTile extends StatelessWidget {
  final IconData icon;
  final String label;
  final String value;

  const _InfoTile({required this.icon, required this.label, required this.value});

  @override
  Widget build(BuildContext context) {
    return ListTile(
      dense: true,
      leading: Icon(icon, size: 20, color: AppTheme.textMuted),
      title: Text(label, style: const TextStyle(fontSize: 13, color: AppTheme.textMuted)),
      trailing: Text(
        value,
        style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600),
      ),
    );
  }
}

