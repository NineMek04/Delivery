import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:jwt_decoder/jwt_decoder.dart';
import 'package:logger/logger.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../config/app_constants.dart';
import 'auth_constants.dart';

part 'auth_service.g.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

/// Authentication state — ใช้ track สถานะ login ของ Rider.
///
/// เทียบกับ:
/// - Angular: `admin-dashboard/src/app/core/services/auth.service.ts`
///   - `isAuthenticated$` → BehaviorSubject
///   - `getToken()` / `setToken()` / `logout()`
///   - Token Clocking (interval check for expiration)
///
/// ใน Flutter ใช้ `flutter_secure_storage` แทน `localStorage`
/// เพราะ mobile ต้องเข้ารหัส token ให้ปลอดภัยกว่า web
enum AuthStatus {
  /// กำลังตรวจสอบ token เริ่มต้น
  loading,

  /// ยืนยันตัวตนแล้ว (มี valid token)
  authenticated,

  /// ยังไม่ได้ login หรือ token หมดอายุ
  unauthenticated,
}

/// AuthService — จัดการ JWT token lifecycle.
///
/// เทียบกับ Angular `AuthService`:
/// ```typescript
/// // Angular version:
/// private readonly TOKEN_KEY = 'delivery_access_token';
/// public getToken(): string | null { return localStorage.getItem(this.TOKEN_KEY); }
/// public setToken(token: string): void { localStorage.setItem(this.TOKEN_KEY, token); }
/// public logout(): void { localStorage.removeItem(this.TOKEN_KEY); }
/// ```
@riverpod
class AuthService extends _$AuthService {
  final _storage = const FlutterSecureStorage();

  /// Cached token เพื่อไม่ต้องอ่าน storage ทุกครั้ง
  String? _cachedToken;

  @override
  AuthStatus build() {
    // เริ่มต้น check token
    _initializeAuth();
    return AuthStatus.loading;
  }

  /// Token ปัจจุบัน (sync access สำหรับ interceptor)
  String? get currentToken => _cachedToken;

  /// ตรวจสอบ token ตอนเปิดแอป
  Future<void> _initializeAuth() async {
    try {
      final token = await _storage.read(key: AppConstants.accessTokenKey);
      if (token != null && !JwtDecoder.isExpired(token)) {
        _cachedToken = token;
        state = AuthStatus.authenticated;
        _logger.i('🔓 Token found and valid — auto-login');
      } else {
        _cachedToken = null;
        if (token != null) {
          // Token มีแต่หมดอายุ → ลบทิ้ง
          await _storage.delete(key: AppConstants.accessTokenKey);
          _logger.w('⏰ Token expired — cleared');
        }
        state = AuthStatus.unauthenticated;
      }
    } catch (e) {
      _logger.e('❌ Error reading token', error: e);
      state = AuthStatus.unauthenticated;
    }
  }

  /// บันทึก token หลัง login สำเร็จ.
  ///
  /// เทียบ Angular: `setToken(token: string)`
  Future<void> setToken(String token) async {
    await _storage.write(key: AppConstants.accessTokenKey, value: token);
    _cachedToken = token;
    state = AuthStatus.authenticated;
    _logger.i('🔑 Token saved');
  }

  /// ล้าง token — logout.
  ///
  /// เทียบ Angular: `logout()`
  Future<void> logout() async {
    await _storage.delete(key: AppConstants.accessTokenKey);
    await _storage.delete(key: AppConstants.refreshTokenKey);
    await _storage.delete(key: AppConstants.userDataKey);
    _cachedToken = null;
    state = AuthStatus.unauthenticated;
    _logger.i('🔒 Logged out — token cleared');
  }

  /// ตรวจสอบว่า token ยังใช้ได้อยู่ไหม (Token Clocking).
  ///
  /// เทียบ Angular: `startTokenClocking()` — interval(5000) subscribe
  bool get isTokenValid {
    if (_cachedToken == null) return false;
    return !JwtDecoder.isExpired(_cachedToken!);
  }

  /// Decode JWT และดึง claims ออกมา.
  ///
  /// เทียบ Angular: `jwtDecode(token)`
  /// เทียบ .NET: JwtTokenService สร้าง claims: NameIdentifier, Email, Name, Role
  Map<String, dynamic>? get decodedToken {
    if (_cachedToken == null) return null;
    try {
      return JwtDecoder.decode(_cachedToken!);
    } catch (e) {
      return null;
    }
  }

  /// ดึง User ID จาก token claims.
  String? get userId => decodedToken?[AuthConstants.claimUserId];

  /// ดึง User Name จาก token claims.
  String? get userName => decodedToken?[AuthConstants.claimName];

  /// ดึง User Role จาก token claims.
  String? get userRole => decodedToken?[AuthConstants.claimRole];

  /// ดึง Email จาก token claims.
  String? get userEmail => decodedToken?[AuthConstants.claimEmail];
}
