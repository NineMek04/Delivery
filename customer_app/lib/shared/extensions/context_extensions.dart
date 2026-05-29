import 'package:flutter/material.dart';

/// Context Extensions — utility methods สำหรับ BuildContext.
///
/// เทียบกับ:
/// - Angular: `admin-dashboard/src/app/core/utils/helper.ts`
extension ContextExtensions on BuildContext {
  // ── Theme shortcuts ────────────────────────────────────────────────

  /// เข้าถึง ThemeData เร็วขึ้น.
  ThemeData get theme => Theme.of(this);

  /// เข้าถึง ColorScheme เร็วขึ้น.
  ColorScheme get colorScheme => Theme.of(this).colorScheme;

  /// เข้าถึง TextTheme เร็วขึ้น.
  TextTheme get textTheme => Theme.of(this).textTheme;

  // ── Size shortcuts ─────────────────────────────────────────────────

  /// ขนาดหน้าจอ.
  Size get screenSize => MediaQuery.sizeOf(this);

  /// ความกว้างหน้าจอ.
  double get screenWidth => MediaQuery.sizeOf(this).width;

  /// ความสูงหน้าจอ.
  double get screenHeight => MediaQuery.sizeOf(this).height;

  /// Padding ของ safe area (notch, status bar, etc.).
  EdgeInsets get safePadding => MediaQuery.paddingOf(this);

  // ── Navigation shortcuts ───────────────────────────────────────────

  /// Pop the current route.
  void pop<T>([T? result]) => Navigator.of(this).pop(result);

  // ── Snackbar shortcuts ─────────────────────────────────────────────

  /// แสดง snackbar สั้นๆ.
  void showSnack(String message) {
    ScaffoldMessenger.of(this).showSnackBar(
      SnackBar(
        content: Text(message),
        behavior: SnackBarBehavior.floating,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
      ),
    );
  }
}
