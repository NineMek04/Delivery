import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/app_theme.dart';
import '../../../shared/widgets/error_dialog.dart';
import '../../../shared/widgets/loading_overlay.dart';
import '../providers/profile_provider.dart';

// ─────────────────────────────────────────────────────────────────────────────
// Profile Screen
// ─────────────────────────────────────────────────────────────────────────────

/// Profile Screen — แสดงข้อมูล Rider + ปุ่ม Logout.
class ProfileScreen extends ConsumerStatefulWidget {
  const ProfileScreen({super.key});

  @override
  ConsumerState<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends ConsumerState<ProfileScreen>
    with SingleTickerProviderStateMixin {
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

  // ── Logout ────────────────────────────────────────────────────────────────

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

  // ── Change Password Dialog ────────────────────────────────────────────────

  Future<void> _showChangePasswordDialog() async {
    final currentPasswordController = TextEditingController();
    final newPasswordController = TextEditingController();
    final confirmPasswordController = TextEditingController();
    final formKey = GlobalKey<FormState>();

    await showDialog(
      context: context,
      barrierDismissible: false,
      builder: (context) {
        return AlertDialog(
          title: const Text('เปลี่ยนรหัสผ่าน'),
          content: Form(
            key: formKey,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  TextFormField(
                    controller: currentPasswordController,
                    obscureText: true,
                    decoration: const InputDecoration(
                      labelText: 'รหัสผ่านปัจจุบัน',
                      prefixIcon: Icon(Icons.lock_outline),
                    ),
                    validator: (v) {
                      if (v == null || v.isEmpty) {
                        return 'กรุณากรอกรหัสผ่านปัจจุบัน';
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),
                  TextFormField(
                    controller: newPasswordController,
                    obscureText: true,
                    decoration: const InputDecoration(
                      labelText: 'รหัสผ่านใหม่',
                      prefixIcon: Icon(Icons.lock_reset),
                    ),
                    validator: (v) {
                      if (v == null || v.isEmpty) {
                        return 'กรุณากรอกรหัสผ่านใหม่';
                      }
                      if (v.length < 6) {
                        return 'รหัสผ่านใหม่ต้องมีอย่างน้อย 6 ตัวอักษร';
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),
                  TextFormField(
                    controller: confirmPasswordController,
                    obscureText: true,
                    decoration: const InputDecoration(
                      labelText: 'ยืนยันรหัสผ่านใหม่',
                      prefixIcon: Icon(Icons.lock_reset),
                    ),
                    validator: (v) {
                      if (v != newPasswordController.text) {
                        return 'รหัสผ่านใหม่ไม่ตรงกัน';
                      }
                      return null;
                    },
                  ),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('ยกเลิก', style: TextStyle(color: Colors.grey)),
            ),
            ElevatedButton(
              onPressed: () async {
                if (formKey.currentState?.validate() == true) {
                  Navigator.pop(context); // close dialog
                  final success = await ref.read(profileNotifierProvider.notifier).changePassword(
                    currentPasswordController.text,
                    newPasswordController.text,
                  );
                  if (mounted) {
                    if (success) {
                      ErrorDialog.showSuccess(
                        context,
                        'เปลี่ยนรหัสผ่านสำเร็จแล้ว ระบบจะนำคุณออกจากระบบเพื่อเข้าสู่ระบบใหม่',
                      );
                      // wait a little bit and logout
                      await Future.delayed(const Duration(seconds: 2));
                      if (mounted) {
                        ref.read(profileNotifierProvider.notifier).logout();
                      }
                    } else {
                      final state = ref.read(profileNotifierProvider);
                      ErrorDialog.show(
                        context,
                        title: 'เปลี่ยนรหัสผ่านล้มเหลว',
                        message: state.error ?? 'เกิดข้อผิดพลาดในการเปลี่ยนรหัสผ่าน',
                      );
                    }
                  }
                }
              },
              child: const Text('ยืนยัน'),
            ),
          ],
        );
      },
    );
  }

  // ── Notification Settings Dialog ──────────────────────────────────────────

  void _showNotificationSettingsDialog() {
    showDialog(
      context: context,
      builder: (context) {
        return Consumer(
          builder: (context, ref, _) {
            final profile = ref.watch(profileNotifierProvider);
            return AlertDialog(
              title: const Text('การตั้งค่าแจ้งเตือน'),
              content: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  SwitchListTile(
                    title: const Text('รับข้อเสนองานใหม่'),
                    subtitle: const Text('แจ้งเตือนเมื่อมีงานเสนอเข้ามา'),
                    value: profile.receiveOffers,
                    onChanged: (val) {
                      ref.read(profileNotifierProvider.notifier).toggleReceiveOffers(val);
                    },
                  ),
                  const Divider(),
                  SwitchListTile(
                    title: const Text('การอัปเดตออเดอร์'),
                    subtitle: const Text('แจ้งเตือนสถานะสินค้า/การจัดส่ง'),
                    value: profile.orderUpdates,
                    onChanged: (val) {
                      ref.read(profileNotifierProvider.notifier).toggleOrderUpdates(val);
                    },
                  ),
                  const Divider(),
                  SwitchListTile(
                    title: const Text('ประกาศจากระบบ'),
                    subtitle: const Text('ข่าวสารและโปรโมชันพิเศษ'),
                    value: profile.systemBroadcasts,
                    onChanged: (val) {
                      ref.read(profileNotifierProvider.notifier).toggleSystemBroadcasts(val);
                    },
                  ),
                ],
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.pop(context),
                  child: const Text('ตกลง'),
                ),
              ],
            );
          },
        );
      },
    );
  }

  // ── Build ─────────────────────────────────────────────────────────────────

  @override
  Widget build(BuildContext context) {
    final profile = ref.watch(profileNotifierProvider);
    final theme = Theme.of(context);

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
                child: _ProfileHeader(
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
                    child: _ErrorBanner(message: profile.error!),
                  ),
                ),

              // ── Info Section ─────────────────────────────────────────────
              SliverToBoxAdapter(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 24, 20, 0),
                  child: _SectionLabel(label: 'ข้อมูลบัญชี'),
                ),
              ),
              SliverToBoxAdapter(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 12, 20, 0),
                  child: _ProfileCard(
                    children: [
                      _ProfileInfoRow(
                        icon: Icons.person_outline,
                        label: 'ชื่อ-นามสกุล',
                        value: profile.fullName ?? '—',
                      ),
                      _Divider(),
                      _ProfileInfoRow(
                        icon: Icons.email_outlined,
                        label: 'อีเมล',
                        value: profile.email ?? '—',
                      ),
                      _Divider(),
                      _ProfileInfoRow(
                        icon: Icons.badge_outlined,
                        label: 'Rider ID',
                        value: _shortId(profile.riderId),
                      ),
                      _Divider(),
                      _ProfileInfoRow(
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
              SliverToBoxAdapter(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 28, 20, 0),
                  child: _SectionLabel(label: 'จัดการบัญชี'),
                ),
              ),
              SliverToBoxAdapter(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 12, 20, 0),
                  child: _ProfileCard(
                    children: [
                      _ActionRow(
                        icon: Icons.lock_outline,
                        label: 'เปลี่ยนรหัสผ่าน',
                        iconColor: AppTheme.infoColor,
                        onTap: _showChangePasswordDialog,
                      ),
                      _Divider(),
                      _ActionRow(
                        icon: Icons.notifications_outlined,
                        label: 'การแจ้งเตือน',
                        iconColor: AppTheme.warningColor,
                        onTap: _showNotificationSettingsDialog,
                      ),
                    ],
                  ),
                ),
              ),

              // ── Logout Button ─────────────────────────────────────────────
              SliverToBoxAdapter(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 32, 20, 40),
                  child: _LogoutButton(
                    isLoading: profile.isLoading,
                    onTap: _confirmLogout,
                  ),
                ),
              ),
            ],
          ),

          // ── Loading Overlay ───────────────────────────────────────────────
          if (profile.isLoading)
            const LoadingOverlay(message: 'กำลังดำเนินการ...'),
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

// ─────────────────────────────────────────────────────────────────────────────
// Sub-widgets
// ─────────────────────────────────────────────────────────────────────────────

/// Hero section: avatar + ชื่อ + อีเมล + badge สถานะ
class _ProfileHeader extends StatelessWidget {
  final String? fullName;
  final String? email;
  final String? role;
  final Animation<double> avatarScale;

  const _ProfileHeader({
    required this.fullName,
    required this.email,
    required this.role,
    required this.avatarScale,
  });

  String get _initials {
    if (fullName == null || fullName!.trim().isEmpty) return 'R';
    final parts = fullName!.trim().split(' ');
    if (parts.length >= 2) {
      return '${parts[0][0]}${parts[1][0]}'.toUpperCase();
    }
    return fullName![0].toUpperCase();
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.fromLTRB(24, 32, 24, 32),
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [
            AppTheme.primaryDark,
            AppTheme.primaryColor,
            AppTheme.primaryLight.withValues(alpha: 0.85),
          ],
        ),
      ),
      child: Column(
        children: [
          // Avatar with elastic animation
          ScaleTransition(
            scale: avatarScale,
            child: Container(
              width: 96,
              height: 96,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: Colors.white.withValues(alpha: 0.2),
                border: Border.all(
                  color: Colors.white.withValues(alpha: 0.5),
                  width: 3,
                ),
                boxShadow: [
                  BoxShadow(
                    color: AppTheme.primaryDark.withValues(alpha: 0.5),
                    blurRadius: 20,
                    offset: const Offset(0, 8),
                  ),
                ],
              ),
              child: Center(
                child: Text(
                  _initials,
                  style: const TextStyle(
                    fontSize: 36,
                    fontWeight: FontWeight.w700,
                    color: Colors.white,
                    letterSpacing: 1,
                  ),
                ),
              ),
            ),
          ),
          const SizedBox(height: 16),
          // Full name
          Text(
            fullName ?? 'Rider',
            style: const TextStyle(
              fontSize: 22,
              fontWeight: FontWeight.w700,
              color: Colors.white,
              letterSpacing: 0.3,
            ),
          ),
          const SizedBox(height: 4),
          // Email
          Text(
            email ?? '',
            style: TextStyle(
              fontSize: 14,
              color: Colors.white.withValues(alpha: 0.8),
            ),
          ),
          const SizedBox(height: 12),
          // Role badge
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 5),
            decoration: BoxDecoration(
              color: Colors.white.withValues(alpha: 0.2),
              borderRadius: BorderRadius.circular(20),
              border: Border.all(
                color: Colors.white.withValues(alpha: 0.3),
              ),
            ),
            child: Text(
              _roleDisplay(role),
              style: const TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.w600,
                color: Colors.white,
                letterSpacing: 0.5,
              ),
            ),
          ),
        ],
      ),
    );
  }

  String _roleDisplay(String? role) {
    switch (role?.toUpperCase()) {
      case 'RIDER':
        return '🏍️  ไรเดอร์';
      case 'ADMIN':
        return '🛡️  ผู้ดูแลระบบ';
      default:
        return '👤  ${role ?? 'ผู้ใช้งาน'}';
    }
  }
}

/// Section label เช่น "ข้อมูลบัญชี"
class _SectionLabel extends StatelessWidget {
  final String label;

  const _SectionLabel({required this.label});

  @override
  Widget build(BuildContext context) {
    return Text(
      label.toUpperCase(),
      style: const TextStyle(
        fontSize: 11,
        fontWeight: FontWeight.w700,
        color: AppTheme.textMuted,
        letterSpacing: 1.2,
      ),
    );
  }
}

/// Card container สำหรับ rows ต่างๆ
class _ProfileCard extends StatelessWidget {
  final List<Widget> children;

  const _ProfileCard({required this.children});

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: AppTheme.surfaceCard,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(
          color: AppTheme.surfaceElevated,
          width: 1,
        ),
      ),
      child: Column(children: children),
    );
  }
}

/// ข้อมูลแถว: icon + label + value
class _ProfileInfoRow extends StatelessWidget {
  final IconData icon;
  final String label;
  final String value;
  final Color? valueColor;

  const _ProfileInfoRow({
    required this.icon,
    required this.label,
    required this.value,
    this.valueColor,
  });

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 16),
      child: Row(
        children: [
          Icon(icon, size: 20, color: AppTheme.primaryColor),
          const SizedBox(width: 14),
          Expanded(
            child: Text(
              label,
              style: const TextStyle(
                fontSize: 14,
                color: AppTheme.textSecondary,
              ),
            ),
          ),
          Text(
            value,
            style: TextStyle(
              fontSize: 14,
              fontWeight: FontWeight.w500,
              color: valueColor ?? AppTheme.textPrimary,
            ),
          ),
        ],
      ),
    );
  }
}

/// Action row: icon + label + chevron
class _ActionRow extends StatelessWidget {
  final IconData icon;
  final String label;
  final Color iconColor;
  final VoidCallback onTap;

  const _ActionRow({
    required this.icon,
    required this.label,
    required this.iconColor,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(16),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 16),
        child: Row(
          children: [
            Container(
              width: 36,
              height: 36,
              decoration: BoxDecoration(
                color: iconColor.withValues(alpha: 0.15),
                borderRadius: BorderRadius.circular(10),
              ),
              child: Icon(icon, size: 20, color: iconColor),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Text(
                label,
                style: const TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w500,
                  color: AppTheme.textPrimary,
                ),
              ),
            ),
            const Icon(
              Icons.chevron_right,
              size: 20,
              color: AppTheme.textMuted,
            ),
          ],
        ),
      ),
    );
  }
}

/// Logout button
class _LogoutButton extends StatelessWidget {
  final bool isLoading;
  final VoidCallback onTap;

  const _LogoutButton({required this.isLoading, required this.onTap});

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: double.infinity,
      child: OutlinedButton.icon(
        onPressed: isLoading ? null : onTap,
        style: OutlinedButton.styleFrom(
          foregroundColor: AppTheme.errorColor,
          side: const BorderSide(color: AppTheme.errorColor, width: 1.5),
          padding: const EdgeInsets.symmetric(vertical: 14),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(14),
          ),
        ),
        icon: const Icon(Icons.logout_outlined, size: 20),
        label: const Text(
          'ออกจากระบบ',
          style: TextStyle(
            fontSize: 16,
            fontWeight: FontWeight.w600,
            letterSpacing: 0.3,
          ),
        ),
      ),
    );
  }
}

/// Thin divider ระหว่าง rows
class _Divider extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return const Divider(
      height: 1,
      thickness: 1,
      indent: 54,
      endIndent: 0,
      color: AppTheme.surfaceElevated,
    );
  }
}

/// Error banner เมื่อโหลดข้อมูลไม่สำเร็จ
class _ErrorBanner extends StatelessWidget {
  final String message;

  const _ErrorBanner({required this.message});

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.only(top: 16),
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      decoration: BoxDecoration(
        color: AppTheme.errorColor.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: AppTheme.errorColor.withValues(alpha: 0.4),
        ),
      ),
      child: Row(
        children: [
          const Icon(Icons.warning_amber_rounded,
              color: AppTheme.errorColor, size: 18),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              message,
              style: const TextStyle(
                fontSize: 13,
                color: AppTheme.errorColor,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
