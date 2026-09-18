import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../../shared/widgets/error_dialog.dart';
import '../providers/profile_provider.dart';

class ProfileDialogs {
  static Future<void> showChangePasswordDialog(
    BuildContext context,
    WidgetRef ref,
  ) async {
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
                  Navigator.pop(context);
                  final success = await ref.read(profileNotifierProvider.notifier).changePassword(
                    currentPasswordController.text,
                    newPasswordController.text,
                  );
                  if (context.mounted) {
                    if (success) {
                      ErrorDialog.showSuccess(
                        context,
                        'เปลี่ยนรหัสผ่านสำเร็จแล้ว ระบบจะนำคุณออกจากระบบเพื่อเข้าสู่ระบบใหม่',
                      );
                      await Future.delayed(const Duration(seconds: 2));
                      if (context.mounted) {
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

  static void showNotificationSettingsDialog(
    BuildContext context,
    WidgetRef ref,
  ) {
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
}
