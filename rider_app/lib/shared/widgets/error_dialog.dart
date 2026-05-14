import 'package:flutter/material.dart';

/// Error Dialog — แสดง dialog แจ้ง error.
///
/// เทียบกับ:
/// - Angular: SweetAlert2 ใน error.interceptor.ts
///
/// Usage:
/// ```dart
/// ErrorDialog.show(
///   context,
///   title: 'เกิดข้อผิดพลาด',
///   message: 'ไม่สามารถเชื่อมต่อ server ได้',
/// );
/// ```
class ErrorDialog {
  ErrorDialog._();

  /// แสดง error dialog.
  static Future<void> show(
    BuildContext context, {
    required String title,
    required String message,
    String? buttonText,
    VoidCallback? onPressed,
  }) {
    return showDialog(
      context: context,
      builder: (context) => AlertDialog(
        icon: Icon(
          Icons.error_outline,
          color: Theme.of(context).colorScheme.error,
          size: 48,
        ),
        title: Text(title),
        content: Text(message),
        actions: [
          TextButton(
            onPressed: onPressed ?? () => Navigator.of(context).pop(),
            child: Text(buttonText ?? 'ตกลง'),
          ),
        ],
      ),
    );
  }

  /// แสดง confirmation dialog.
  static Future<bool?> showConfirm(
    BuildContext context, {
    required String title,
    required String message,
    String? confirmText,
    String? cancelText,
  }) {
    return showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        icon: Icon(
          Icons.help_outline,
          color: Theme.of(context).colorScheme.primary,
          size: 48,
        ),
        title: Text(title),
        content: Text(message),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: Text(cancelText ?? 'ยกเลิก'),
          ),
          ElevatedButton(
            onPressed: () => Navigator.of(context).pop(true),
            child: Text(confirmText ?? 'ยืนยัน'),
          ),
        ],
      ),
    );
  }

  /// แสดง success snackbar.
  static void showSuccess(BuildContext context, String message) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Row(
          children: [
            const Icon(Icons.check_circle, color: Colors.white),
            const SizedBox(width: 8),
            Expanded(child: Text(message)),
          ],
        ),
        backgroundColor: Colors.green,
        behavior: SnackBarBehavior.floating,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
      ),
    );
  }
}
