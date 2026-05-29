import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

/// App Theme — Design System สำหรับ Customer App.
///
/// เทียบกับ:
/// - Angular: global CSS / SCSS theme
/// - BackendApi: ไม่มี (server-side)
///
/// ใช้ Light theme เป็นหลักเพื่อให้ดูสะอาดตาสำหรับลูกค้า
class AppTheme {
  AppTheme._();

  // ── Color Palette ──────────────────────────────────────────────────
  static const Color primaryColor = Color(0xFFFF5722);      // Deep Orange
  static const Color primaryLight = Color(0xFFFF8A65);
  static const Color primaryDark = Color(0xFFE64A19);

  static const Color accentColor = Color(0xFFFFC107);       // Amber
  static const Color warningColor = Color(0xFFF59E0B);      // Amber (กำลังส่ง)
  static const Color errorColor = Color(0xFFEF4444);        // Red (Error/Offline)
  static const Color infoColor = Color(0xFF3B82F6);         // Blue (Info)

  static const Color surfaceDark = Color(0xFF1E1E2E);
  static const Color surfaceCard = Color(0xFF2A2A3C);
  static const Color surfaceElevated = Color(0xFF363650);

  static const Color textPrimary = Color(0xFFF1F5F9);
  static const Color textSecondary = Color(0xFF94A3B8);
  static const Color textMuted = Color(0xFF64748B);

  // ── Status Colors (ตรงกับ Rider/Order status ใน BackendApi) ──────
  static const Map<String, Color> riderStatusColors = {
    'IDLE': accentColor,
    'AVAILABLE': accentColor,
    'RESERVED': warningColor,
    'BUSY': warningColor,
    'DELIVERING': warningColor,
    'OFFLINE': textMuted,
  };

  static const Map<String, Color> orderStatusColors = {
    'CREATED': textMuted,
    'MATCHING': infoColor,
    'OFFERING': warningColor,
    'ASSIGNED': infoColor,
    'PICKING_UP': primaryColor,
    'DELIVERING': primaryLight,
    'COMPLETED': accentColor,
    'CANCELLED': errorColor,
  };

  // ── Dark Theme ─────────────────────────────────────────────────────
  static ThemeData get darkTheme {
    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,

      // Color scheme
      colorScheme: const ColorScheme.dark(
        primary: primaryColor,
        onPrimary: Colors.white,
        secondary: accentColor,
        onSecondary: Colors.white,
        error: errorColor,
        surface: surfaceDark,
        onSurface: textPrimary,
      ),

      // Scaffold
      scaffoldBackgroundColor: surfaceDark,

      // Typography — Google Fonts
      textTheme: GoogleFonts.interTextTheme(
        ThemeData.dark().textTheme,
      ).copyWith(
        headlineLarge: GoogleFonts.inter(
          fontSize: 28,
          fontWeight: FontWeight.w700,
          color: textPrimary,
        ),
        headlineMedium: GoogleFonts.inter(
          fontSize: 22,
          fontWeight: FontWeight.w600,
          color: textPrimary,
        ),
        titleLarge: GoogleFonts.inter(
          fontSize: 18,
          fontWeight: FontWeight.w600,
          color: textPrimary,
        ),
        titleMedium: GoogleFonts.inter(
          fontSize: 16,
          fontWeight: FontWeight.w500,
          color: textPrimary,
        ),
        bodyLarge: GoogleFonts.inter(
          fontSize: 16,
          color: textPrimary,
        ),
        bodyMedium: GoogleFonts.inter(
          fontSize: 14,
          color: textSecondary,
        ),
        labelLarge: GoogleFonts.inter(
          fontSize: 14,
          fontWeight: FontWeight.w600,
          color: textPrimary,
        ),
      ),

      // AppBar
      appBarTheme: AppBarTheme(
        backgroundColor: surfaceDark,
        foregroundColor: textPrimary,
        elevation: 0,
        centerTitle: true,
        titleTextStyle: GoogleFonts.inter(
          fontSize: 18,
          fontWeight: FontWeight.w600,
          color: textPrimary,
        ),
      ),

      // Cards
      cardTheme: CardThemeData(
        color: surfaceCard,
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16),
        ),
      ),

      // Elevated Buttons
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          backgroundColor: primaryColor,
          foregroundColor: Colors.white,
          elevation: 0,
          padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 14),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(12),
          ),
          textStyle: GoogleFonts.inter(
            fontSize: 16,
            fontWeight: FontWeight.w600,
          ),
        ),
      ),

      // Input fields
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: surfaceCard,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: BorderSide.none,
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: const BorderSide(color: primaryColor, width: 2),
        ),
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
        hintStyle: GoogleFonts.inter(color: textMuted),
      ),

      // Bottom Navigation
      bottomNavigationBarTheme: const BottomNavigationBarThemeData(
        backgroundColor: surfaceCard,
        selectedItemColor: primaryColor,
        unselectedItemColor: textMuted,
        type: BottomNavigationBarType.fixed,
        elevation: 8,
      ),

      // Floating Action Button
      floatingActionButtonTheme: const FloatingActionButtonThemeData(
        backgroundColor: primaryColor,
        foregroundColor: Colors.white,
        elevation: 4,
      ),

      // Divider
      dividerTheme: const DividerThemeData(
        color: surfaceElevated,
        thickness: 1,
      ),
    );
  }

  // ── Light Theme (เผื่อไว้) ──────────────────────────────────────────
  static ThemeData get lightTheme {
    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.light,
      colorScheme: ColorScheme.fromSeed(
        seedColor: primaryColor,
        brightness: Brightness.light,
      ),
      textTheme: GoogleFonts.interTextTheme(),
    );
  }
}
