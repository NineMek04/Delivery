import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/api/services/auth_api_service.dart';
import '../../../core/auth/auth_service.dart';
import '../../../core/auth/safe_storage.dart';
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

  /// โหลดข้อมูล Rider profile จาก AuthService (JWT claims + stored user data) และ SafeStorage.
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

      // โหลดการตั้งค่าการแจ้งเตือนจาก Local Storage
      final storage = SafeStorage();
      final receiveOffers = (await storage.read(key: 'notif_receive_offers')) != 'false';
      final orderUpdates = (await storage.read(key: 'notif_order_updates')) != 'false';
      final systemBroadcasts = (await storage.read(key: 'notif_system_broadcasts')) != 'false';

      state = state.copyWith(
        isLoading: false,
        fullName: name,
        email: email,
        riderId: riderId,
        role: role,
        receiveOffers: receiveOffers,
        orderUpdates: orderUpdates,
        systemBroadcasts: systemBroadcasts,
      );
    } catch (e) {
      state = state.copyWith(
        isLoading: false,
        error: 'ไม่สามารถโหลดข้อมูลได้: $e',
      );
    }
  }

  /// เปิด/ปิดการรับงานเสนอ
  Future<void> toggleReceiveOffers(bool value) async {
    state = state.copyWith(receiveOffers: value);
    await SafeStorage().write(key: 'notif_receive_offers', value: value.toString());
  }

  /// เปิด/ปิดการอัปเดตสถานะออเดอร์
  Future<void> toggleOrderUpdates(bool value) async {
    state = state.copyWith(orderUpdates: value);
    await SafeStorage().write(key: 'notif_order_updates', value: value.toString());
  }

  /// เปิด/ปิดการประกาศระบบ
  Future<void> toggleSystemBroadcasts(bool value) async {
    state = state.copyWith(systemBroadcasts: value);
    await SafeStorage().write(key: 'notif_system_broadcasts', value: value.toString());
  }

  /// เปลี่ยนรหัสผ่านทาง API หลังบ้าน
  Future<bool> changePassword(String currentPassword, String newPassword) async {
    state = state.copyWith(isLoading: true, error: null);
    try {
      final authApi = ref.read(authApiServiceProvider);
      await authApi.changePassword(
        currentPassword: currentPassword,
        newPassword: newPassword,
      );
      state = state.copyWith(isLoading: false);
      return true;
    } catch (e) {
      state = state.copyWith(isLoading: false, error: e.toString());
      return false;
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

  // ตั้งค่าการแจ้งเตือน
  final bool receiveOffers;
  final bool orderUpdates;
  final bool systemBroadcasts;

  const ProfileState({
    this.isLoading = false,
    this.error,
    this.fullName,
    this.email,
    this.riderId,
    this.role,
    this.receiveOffers = true,
    this.orderUpdates = true,
    this.systemBroadcasts = true,
  });

  ProfileState copyWith({
    bool? isLoading,
    String? error,
    String? fullName,
    String? email,
    String? riderId,
    String? role,
    bool? receiveOffers,
    bool? orderUpdates,
    bool? systemBroadcasts,
  }) {
    return ProfileState(
      isLoading: isLoading ?? this.isLoading,
      error: error,
      fullName: fullName ?? this.fullName,
      email: email ?? this.email,
      riderId: riderId ?? this.riderId,
      role: role ?? this.role,
      receiveOffers: receiveOffers ?? this.receiveOffers,
      orderUpdates: orderUpdates ?? this.orderUpdates,
      systemBroadcasts: systemBroadcasts ?? this.systemBroadcasts,
    );
  }
}
