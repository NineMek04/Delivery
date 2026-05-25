import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/auth/auth_service.dart';
import '../../../features/auth/providers/auth_provider.dart';
import '../../../models/auth_response.dart';

// ─────────────────────────────────────────────────────────────────────────────
// Provider
// ─────────────────────────────────────────────────────────────────────────────

final profileNotifierProvider =
    NotifierProvider<ProfileNotifier, ProfileState>(ProfileNotifier.new);

// ─────────────────────────────────────────────────────────────────────────────
// Notifier
// ─────────────────────────────────────────────────────────────────────────────

/// Profile feature — โหลดข้อมูล Rider จาก SecureStorage + JWT claims
/// และ expose logout action ผ่าน [AuthNotifier].
class ProfileNotifier extends Notifier<ProfileState> {
  @override
  ProfileState build() {
    // โหลดข้อมูลทันทีที่ provider ถูกสร้าง
    Future.microtask(loadProfile);
    return const ProfileState();
  }

  /// โหลดข้อมูล Rider profile จาก AuthService (JWT claims + stored user data).
  Future<void> loadProfile() async {
    state = state.copyWith(isLoading: true, error: null);

    try {
      final authService = ref.read(authServiceProvider.notifier);

      // ลอง getUserData() จาก SecureStorage ก่อน (ข้อมูลเต็ม)
      final userData = await authService.getUserData();
      UserInfo? userInfo;
      if (userData != null) {
        userInfo = UserInfo.fromJson(userData);
      }

      // Fallback → อ่านจาก JWT claims โดยตรง
      final name = userInfo?.fullName ?? authService.userName;
      final email = userInfo?.email ?? authService.userEmail;
      final riderId = userInfo?.id ?? authService.userId;
      final role = authService.userRole;

      state = state.copyWith(
        isLoading: false,
        fullName: name,
        email: email,
        riderId: riderId,
        role: role,
      );
    } catch (e) {
      state = state.copyWith(
        isLoading: false,
        error: 'ไม่สามารถโหลดข้อมูลได้: $e',
      );
    }
  }

  /// ออกจากระบบ — delegate ไปยัง [AuthNotifier].
  Future<void> logout() async {
    state = state.copyWith(isLoading: true, error: null);
    try {
      await ref.read(authNotifierProvider.notifier).logout();
    } catch (e) {
      state = state.copyWith(isLoading: false, error: e.toString());
    }
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// State
// ─────────────────────────────────────────────────────────────────────────────

/// State สำหรับ Profile Screen.
class ProfileState {
  final bool isLoading;
  final String? error;

  final String? fullName;
  final String? email;
  final String? riderId;
  final String? role;

  const ProfileState({
    this.isLoading = false,
    this.error,
    this.fullName,
    this.email,
    this.riderId,
    this.role,
  });

  ProfileState copyWith({
    bool? isLoading,
    String? error,
    String? fullName,
    String? email,
    String? riderId,
    String? role,
  }) {
    return ProfileState(
      isLoading: isLoading ?? this.isLoading,
      error: error,
      fullName: fullName ?? this.fullName,
      email: email ?? this.email,
      riderId: riderId ?? this.riderId,
      role: role ?? this.role,
    );
  }
}
