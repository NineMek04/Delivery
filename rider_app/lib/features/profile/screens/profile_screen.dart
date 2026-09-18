import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/app_theme.dart';
import '../../../shared/widgets/error_dialog.dart';
import '../../../shared/widgets/loading_overlay.dart';
import '../providers/profile_provider.dart';
import '../widgets/profile_dialogs.dart';
import '../widgets/profile_header.dart';
import '../widgets/profile_card_widgets.dart';

/// Profile Screen — แสดงข้อมูล Rider + ปุ่ม Logout.
class ProfileScreen extends ConsumerStatefulWidget {
  const ProfileScreen({super.key});

  @override
  ConsumerState<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends ConsumerState<ProfileScreen>
    with TickerProviderStateMixin {
  late final AnimationController _avatarController;
  late final Animation<double> _avatarScale;

  @override
  void initState() {
    super.initState();
    _avatarController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 600),
    );
    _avatarScale = CurvedAnimation(
      parent: _avatarController,
      curve: Curves.elasticOut,
    );
    _avatarController.forward();
  }

  @override
  void dispose() {
    _avatarController.dispose();
    super.dispose();
  }

  Future<void> _confirmLogout() async {
    final ok = await ErrorDialog.showConfirm(
      context,
      title: 'ออกจากระบบ',
      message: 'ต้องการออกจากระบบใช่หรือไม่?',
      confirmText: 'ออกจากระบบ',
    );
    if (ok == true && mounted) {
      await ref.read(profileNotifierProvider.notifier).logout();
    }
  }

  @override
  Widget build(BuildContext context) {
    final profile = ref.watch(profileNotifierProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text('โปรไฟล์'),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            tooltip: 'รีเฟรช',
            onPressed: () =>
                ref.read(profileNotifierProvider.notifier).loadProfile(),
          ),
        ],
      ),
      body: Stack(
        children: [
          CustomScrollView(
            physics: const BouncingScrollPhysics(),
            slivers: [
              // ── Hero Header ──────────────────────────────────────────────
              SliverToBoxAdapter(
                child: ProfileHeader(
                  fullName: profile.fullName,
                  email: profile.email,
                  role: profile.role,
                  avatarScale: _avatarScale,
                ),
              ),

              // ── Error Banner ─────────────────────────────────────────────
              if (profile.error != null)
                SliverToBoxAdapter(
                  child: Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 20),
                    child: ErrorBanner(message: profile.error!),
                  ),
                ),

              // ── Info Section ─────────────────────────────────────────────
              const SliverToBoxAdapter(
                child: Padding(
                  padding: EdgeInsets.fromLTRB(20, 24, 20, 0),
                  child: SectionLabel(label: 'ข้อมูลบัญชี'),
                ),
              ),
              SliverToBoxAdapter(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 12, 20, 0),
                  child: ProfileCard(
                    children: [
                      ProfileInfoRow(
                        icon: Icons.person_outline,
                        label: 'ชื่อ-นามสกุล',
                        value: profile.fullName ?? '—',
                      ),
                      const ProfileDivider(),
                      ProfileInfoRow(
                        icon: Icons.email_outlined,
                        label: 'อีเมล',
                        value: profile.email ?? '—',
                      ),
                      const ProfileDivider(),
                      ProfileInfoRow(
                        icon: Icons.badge_outlined,
                        label: 'Rider ID',
                        value: _shortId(profile.riderId),
                      ),
                      const ProfileDivider(),
                      ProfileInfoRow(
                        icon: Icons.shield_outlined,
                        label: 'สิทธิ์การใช้งาน',
                        value: _roleLabel(profile.role),
                        valueColor: AppTheme.accentColor,
                      ),
                    ],
                  ),
                ),
              ),

              // ── Account Actions ───────────────────────────────────────────
              const SliverToBoxAdapter(
                child: Padding(
                  padding: EdgeInsets.fromLTRB(20, 28, 20, 0),
                  child: SectionLabel(label: 'จัดการบัญชี'),
                ),
              ),
              SliverToBoxAdapter(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 12, 20, 0),
                  child: ProfileCard(
                    children: [
                      ActionRow(
                        icon: Icons.lock_outline,
                        label: 'เปลี่ยนรหัสผ่าน',
                        iconColor: AppTheme.infoColor,
                        onTap: () => ProfileDialogs.showChangePasswordDialog(context, ref),
                      ),
                      const ProfileDivider(),
                      ActionRow(
                        icon: Icons.notifications_outlined,
                        label: 'การแจ้งเตือน',
                        iconColor: AppTheme.warningColor,
                        onTap: () => ProfileDialogs.showNotificationSettingsDialog(context, ref),
                      ),
                      const ProfileDivider(),
                      ActionRow(
                        icon: Icons.dns_outlined,
                        label: 'ตั้งค่า Server URL',
                        iconColor: AppTheme.primaryColor,
                        onTap: () => context.push('/server-settings'),
                      ),
                    ],
                  ),
                ),
              ),

              // ── Logout Button ─────────────────────────────────────────────
              SliverToBoxAdapter(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 32, 20, 40),
                  child: LogoutButton(
                    isLoading: profile.isLoading,
                    onTap: _confirmLogout,
                  ),
                ),
              ),
            ],
          ),

          // ── Loading Overlay ───────────────────────────────────────────────
          if (profile.isLoading && profile.fullName == null)
            const LoadingOverlay(message: 'กำลังโหลดข้อมูล...'),
        ],
      ),
    );
  }

  // ── Helpers ───────────────────────────────────────────────────────────────

  String _shortId(String? id) {
    if (id == null || id.isEmpty) return '—';
    if (id.length <= 8) return id;
    return '${id.substring(0, 8)}…';
  }

  String _roleLabel(String? role) {
    switch (role?.toUpperCase()) {
      case 'RIDER':
        return 'ไรเดอร์';
      case 'ADMIN':
        return 'ผู้ดูแลระบบ';
      default:
        return role ?? '—';
    }
  }
}
