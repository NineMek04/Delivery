import 'package:flutter/material.dart';
import 'app_theme_dark.dart';
import 'app_theme_light.dart';
/// App Theme — Design System สำหรับ Rider App.
///
/// เทียบกับ:
/// - Angular: global CSS / SCSS theme
/// - BackendApi: ไม่มี (server-side)
///
/// ปรับปรุงใหม่: ใช้สีเขียวมรกต (Emerald Green) เป็นสีหลัก มินิมอล โมเดิร์น ดูง่าย
class AppTheme {
  AppTheme._();

  // ── Color Palette ──────────────────────────────────────────────────
  static const Color primaryColor = Color(0xFF10B981);      // Emerald Green (เขียวหลัก)
  static const Color primaryLight = Color(0xFF34D399);      // Mint Green
  static const Color primaryDark = Color(0xFF059669);       // Forest Green

  static const Color accentColor = Color(0xFF10B981);       // Emerald (สำเร็จ/Online)
  static const Color warningColor = Color(0xFFF59E0B);      // Amber (กำลังส่ง)
  static const Color errorColor = Color(0xFFEF4444);        // Red (Error/Offline)
  static const Color infoColor = Color(0xFF3B82F6);         // Blue (Info)

  // Dark Theme Surfaces (มินิมอลโมเดิร์น โทนน้ำเงิน/ดำเข้ม)
  static const Color surfaceDark = Color(0xFF0B0F19);
  static const Color surfaceCard = Color(0xFF151F32);
  static const Color surfaceElevated = Color(0xFF1E293B);
  static const Color borderColor = Color(0xFF1E293B);

  // Light Theme Surfaces (มินิมอลโมเดิร์น โทนขาวนวลและเทาอ่อน)
  static const Color lightBackground = Color(0xFFF8FAFC);
  static const Color lightSurface = Color(0xFFFFFFFF);
  static const Color lightBorder = Color(0xFFE2E8F0);

  // Text Colors
  static const Color textPrimary = Color(0xFFF1F5F9);
  static const Color textSecondary = Color(0xFF94A3B8);
  static const Color textMuted = Color(0xFF64748B);

  static const Color textLightPrimary = Color(0xFF0F172A);
  static const Color textLightSecondary = Color(0xFF475569);
  static const Color textLightMuted = Color(0xFF94A3B8);

  // ── Status Colors (ตรงกับ Rider/Order status ใน BackendApi) ──────
  static const Map<String, Color> riderStatusColors = {
    'IDLE': accentColor,
    'RESERVED': warningColor,
    'BUSY': warningColor,
    'STALE': textMuted,
    'OFFLINE': textMuted,
  };

  static const Map<String, Color> orderStatusColors = {
    'CREATED': textMuted,
    'MATCHING': infoColor,
    'OFFERING': warningColor,
    'ASSIGNED': infoColor,
    'PICKING_UP': primaryDark,
    'DELIVERING': primaryColor,
    'COMPLETED': accentColor,
    'CANCELLED': errorColor,
  };

  // ── Dark Theme (ไรเดอร์ & ร้านค้า) ──────────────────────────────────
  // ── Dark Theme (ไรเดอร์ / ร้านค้า) ──────────────────────────────────
  static ThemeData get darkTheme => AppDarkTheme.theme;

  // ── Light Theme (ลูกค้า) ───────────────────────────────────────────
  static ThemeData get lightTheme => AppLightTheme.theme;
}
