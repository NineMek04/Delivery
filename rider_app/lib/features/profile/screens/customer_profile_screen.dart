import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import '../../../app/app_theme.dart';
import '../../../core/auth/auth_service.dart';
import '../../auth/providers/auth_provider.dart';

class CustomerProfileScreen extends ConsumerWidget {
  const CustomerProfileScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final authState = ref.watch(authServiceProvider.notifier);
    final user = authState.currentUser;

    return Scaffold(
      appBar: AppBar(
        title: const Text('โปรไฟล์ของคุณ'),
        actions: [
          IconButton(
            icon: const Icon(Icons.logout, color: Colors.red),
            onPressed: () => ref.read(authNotifierProvider.notifier).logout(),
          ),
        ],
      ),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(16),
        child: Column(
          children: [
            // User Header
            const Center(
              child: CircleAvatar(
                radius: 50,
                backgroundColor: AppTheme.primaryColor,
                child: Icon(Icons.person, size: 60, color: Colors.white),
              ),
            ),
            const SizedBox(height: 16),
            Text(
              user?.fullName ?? 'ชื่อผู้ใช้งาน',
              style: const TextStyle(fontSize: 24, fontWeight: FontWeight.bold),
            ),
            Text(
              user?.email ?? 'email@example.com',
              style: const TextStyle(color: Colors.grey),
            ),
            const SizedBox(height: 32),

            // Profile Options
            _buildOption(
              context,
              icon: Icons.location_on_outlined,
              title: 'ที่อยู่ของฉัน',
              subtitle: 'จัดการที่อยู่จัดส่ง',
              onTap: () => context.pushNamed('customerAddresses'),
            ),
            _buildOption(
              context,
              icon: Icons.history,
              title: 'ประวัติการสั่งซื้อ',
              subtitle: 'ดูออเดอร์ทั้งหมดที่เคยสั่ง',
              onTap: () => context.goNamed('customerOrders'),
            ),
            _buildOption(
              context,
              icon: Icons.payment,
              title: 'วิธีการชำระเงิน',
              subtitle: 'จัดการบัตรและกระเป๋าเงิน',
              onTap: () {},
            ),
            _buildOption(
              context,
              icon: Icons.notifications_none,
              title: 'การแจ้งเตือน',
              subtitle: 'ตั้งค่าการแจ้งเตือนในแอป',
              onTap: () {},
            ),
            _buildOption(
              context,
              icon: Icons.help_outline,
              title: 'ความช่วยเหลือ',
              subtitle: 'ศูนย์ช่วยเหลือและคำถามที่พบบ่อย',
              onTap: () {},
            ),
            
            const SizedBox(height: 32),
            SizedBox(
              width: double.infinity,
              child: OutlinedButton(
                onPressed: () => ref.read(authNotifierProvider.notifier).logout(),
                style: OutlinedButton.styleFrom(
                  foregroundColor: Colors.red,
                  side: const BorderSide(color: Colors.red),
                  padding: const EdgeInsets.symmetric(vertical: 12),
                ),
                child: const Text('ออกจากระบบ'),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildOption(
    BuildContext context, {
    required IconData icon,
    required String title,
    required String subtitle,
    required VoidCallback onTap,
  }) {
    return ListTile(
      leading: Container(
        padding: const EdgeInsets.all(8),
        decoration: BoxDecoration(
          color: Colors.grey[100],
          borderRadius: BorderRadius.circular(8),
        ),
        child: Icon(icon, color: AppTheme.primaryColor),
      ),
      title: Text(title, style: const TextStyle(fontWeight: FontWeight.bold)),
      subtitle: Text(subtitle, style: const TextStyle(fontSize: 12)),
      trailing: const Icon(Icons.chevron_right, size: 20),
      onTap: onTap,
    );
  }
}
