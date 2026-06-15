import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'safe_storage.dart';
import 'package:jwt_decoder/jwt_decoder.dart';
import 'package:logger/logger.dart';
import '../database/local_database_service.dart';
import '../../models/auth_response.dart';
import '../api/api_helpers.dart';
import '../api/delivery_api_client.dart';
import '../config/app_constants.dart';
import '../config/environment.dart';
import 'auth_constants.dart';
import '../location/gps_buffer_service.dart';

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

/// AuthService — จัดการ JWT token lifecycle + Refresh Token + Token Clocking.
///
/// เทียบกับ Angular `AuthService`:
/// ```typescript
/// // Angular version:
/// private readonly TOKEN_KEY = 'delivery_access_token';
/// public getToken(): string | null { return localStorage.getItem(this.TOKEN_KEY); }
/// public setToken(token: string): void { localStorage.setItem(this.TOKEN_KEY, token); }
/// public logout(): void { localStorage.removeItem(this.TOKEN_KEY); }
/// private startTokenClocking(): void { interval(5000).subscribe(...); }
/// ```
///
/// ฟีเจอร์เพิ่มเติมจาก Angular version:
/// 1. **Refresh Token** — ขอ Access Token ใหม่อัตโนมัติเมื่อหมดอายุ
/// 2. **Token Clocking** — ตรวจสอบ token expiry เป็นระยะ ๆ (เทียบ Angular interval(5000))
/// 3. **User Data Management** — จัดเก็บ/ดึงข้อมูลผู้ใช้จาก SecureStorage
/// 4. **Proactive Refresh** — refresh token ก่อนหมดอายุ 2 นาที
class AuthService extends Notifier<AuthStatus> {
  final _storage = SafeStorage();

  /// Cached token เพื่อไม่ต้องอ่าน storage ทุกครั้ง
  String? _cachedToken;

  /// Timer สำหรับ Token Clocking (เทียบ Angular: clockingSubscription)
  Timer? _clockingTimer;

  /// Future สำหรับป้องกัน concurrent refresh และแชร์ผลลัพธ์ร่วมกัน
  Future<bool>? _refreshFuture;

  /// Prevents a stale async startup check from overwriting a newer login/logout.
  int _authMutationVersion = 0;

  @override
  AuthStatus build() {
    // Cleanup timer เมื่อ provider ถูก dispose
    ref.onDispose(() {
      _stopTokenClocking();
    });

    // เริ่มต้น check token
    _initializeAuth();
    return AuthStatus.loading;
  }

  // ═══════════════════════════════════════════════════════════════════
  // Token Access (Sync)
  // ═══════════════════════════════════════════════════════════════════

  /// Token ปัจจุบัน (sync access สำหรับ interceptor)
  String? get currentToken => _cachedToken;

  /// ตรวจสอบว่า token ยังใช้ได้อยู่ไหม
  bool get isTokenValid {
    if (_cachedToken == null) return false;
    try {
      return !JwtDecoder.isExpired(_cachedToken!);
    } catch (e) {
      _logger.e('❌ Invalid token format', error: e);
      return false;
    }
  }

  // ═══════════════════════════════════════════════════════════════════
  // JWT Decode & Claims
  // ═══════════════════════════════════════════════════════════════════

  /// Decode JWT และดึง claims ออกมา.
  ///
  /// เทียบ Angular: `jwtDecode(token)`
  /// เทียบ .NET: JwtTokenService สร้าง claims: NameIdentifier, Email, Name, Role
  Map<String, dynamic>? get decodedToken {
    if (_cachedToken == null) return null;
    try {
      return JwtDecoder.decode(_cachedToken!);
    } catch (e) {
      _logger.e('❌ Failed to decode token', error: e);
      return null;
    }
  }

  /// ดึง User ID จาก token claims.
  String? get userId =>
      decodedToken?[AuthConstants.claimUserId] ??
      decodedToken?['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'];

  /// ดึง User Name จาก token claims.
  String? get userName =>
      decodedToken?[AuthConstants.claimName] ??
      decodedToken?['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'];

  /// ดึง User Role จาก token claims.
  String? get userRole =>
      decodedToken?[AuthConstants.claimRole] ??
      decodedToken?['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];

  /// ดึง Email จาก token claims.
  String? get userEmail =>
      decodedToken?[AuthConstants.claimEmail] ??
      decodedToken?['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress'];

  /// ดึงข้อมูลผู้ใช้ปัจจุบัน (UserInfo)
  UserInfo? get currentUser {
    final claims = decodedToken;
    if (claims == null) return null;
    return UserInfo(
      id: userId ?? '',
      email: userEmail ?? '',
      fullName: userName ?? '',
      role: userRole ?? '',
    );
  }

  // ═══════════════════════════════════════════════════════════════════
  // Token Lifecycle — Set / Refresh / Logout
  // ═══════════════════════════════════════════════════════════════════

  /// บันทึก tokens หลัง login/register สำเร็จ.
  ///
  /// เทียบ Angular: `setToken(token: string)`
  ///
  /// [accessToken] — JWT Access Token
  /// [refreshToken] — Refresh Token สำหรับขอ Access Token ใหม่
  /// [userData] — ข้อมูลผู้ใช้ (optional, จาก AuthResponse.User)
  Future<void> setTokens({
    required String accessToken,
    required String refreshToken,
    Map<String, dynamic>? userData,
  }) async {
    _authMutationVersion++;

    await _storage.write(key: AppConstants.accessTokenKey, value: accessToken);
    await _storage.write(
      key: AppConstants.refreshTokenKey,
      value: refreshToken,
    );

    if (userData != null) {
      await _storage.write(
        key: AppConstants.userDataKey,
        value: jsonEncode(userData),
      );
    }

    _cachedToken = accessToken;
    state = AuthStatus.authenticated;

    // เริ่ม Token Clocking หลัง set token
    _startTokenClocking();

    _logger.i('🔑 Tokens saved — access + refresh');
  }

  /// Backward-compatible setToken (เก็บแค่ Access Token)
  ///
  /// เทียบ Angular: `setToken(token: string)`
  Future<void> setToken(String token) async {
    _authMutationVersion++;
    await _storage.write(key: AppConstants.accessTokenKey, value: token);
    _cachedToken = token;
    state = AuthStatus.authenticated;
    _startTokenClocking();
    _logger.i('🔑 Token saved');
  }

  /// ขอ Access Token ใหม่โดยใช้ Refresh Token.
  ///
  /// เทียบ .NET: `POST /api/v1/auth/refresh`
  ///
  /// Flow:
  /// 1. อ่าน Refresh Token จาก SecureStorage
  /// 2. เรียก API refresh endpoint
  /// 3. หากสำเร็จ → บันทึก token ชุดใหม่
  /// 4. หากล้มเหลว → logout (บังคับ re-login)
  Future<bool> refreshAccessToken() async {
    // ป้องกัน concurrent refresh (race condition)
    if (_refreshFuture != null) {
      _logger.d(
        '⏳ Token refresh already in progress, awaiting current refresh future...',
      );
      return await _refreshFuture!;
    }

    final refreshVersion = _authMutationVersion;
    final completer = Completer<bool>();
    _refreshFuture = completer.future;

    try {
      final refreshToken = await _storage.read(
        key: AppConstants.refreshTokenKey,
      );

      if (refreshToken == null || refreshToken.isEmpty) {
        _logger.w('⚠️ No refresh token found — forcing re-login');
        await _forceLogout();
        completer.complete(false);
        return false;
      }

      // สร้าง Dio instance แยกเพื่อหลีกเลี่ยง interceptor loop
      final baseUrl = ref.read(deliveryApiClientProvider).options.baseUrl;
      final dio = Dio(
        BaseOptions(
          baseUrl: baseUrl,
          connectTimeout: Environment.connectTimeout,
          receiveTimeout: Environment.receiveTimeout,
          headers: {
            'Content-Type': 'application/json',
            'Accept': 'application/json',
            'X-Client-Type': 'RiderApp',
          },
        ),
      );

      final response = await dio.post(
        '${AppConstants.authEndpoint}/refresh',
        data: {'RefreshToken': refreshToken},
      );

      final parsed = parseApiResponse(response.data, AuthResponse.fromJson);
      if (parsed.success && parsed.value != null) {
        if (refreshVersion != _authMutationVersion) {
          _logger.d('Token refresh superseded by a newer auth action');
          completer.complete(false);
          return false;
        }

        final auth = parsed.value!;
        await setTokens(
          accessToken: auth.accessToken,
          refreshToken: auth.refreshToken,
          userData: auth.user.toJson(),
        );

        _logger.i('🔄 Token refreshed successfully');
        completer.complete(true);
        return true;
      } else {
        _logger.w('⚠️ Refresh API returned failure: ${parsed.message}');
        if (refreshVersion == _authMutationVersion) {
          await _forceLogout();
        }
        completer.complete(false);
        return false;
      }
    } on DioException catch (e) {
      _logger.e(
        '❌ Token refresh failed (${e.response?.statusCode})',
        error: e.message,
      );
      final statusCode = e.response?.statusCode;
      final credentialsRejected =
          statusCode == 400 || statusCode == 401 || statusCode == 403;
      if (credentialsRejected && refreshVersion == _authMutationVersion) {
        await _forceLogout();
      }
      completer.complete(false);
      return false;
    } catch (e) {
      _logger.e('❌ Unexpected error during token refresh', error: e);
      completer.complete(false);
      return false;
    } finally {
      _refreshFuture = null;
    }
  }

  /// ล้าง token — logout.
  ///
  /// เทียบ Angular: `logout()`
  Future<void> logout() async {
    _authMutationVersion++;
    _stopTokenClocking();
    try {
      await ref.read(localDatabaseServiceProvider).clearAllData();
    } catch (_) {}
    try {
      ref.read(gpsBufferServiceProvider).clearBuffer();
    } catch (_) {}
    await _storage.delete(key: AppConstants.accessTokenKey);
    await _storage.delete(key: AppConstants.refreshTokenKey);
    await _storage.delete(key: AppConstants.userDataKey);
    _cachedToken = null;
    state = AuthStatus.unauthenticated;
    _logger.i('🔒 Logged out — tokens cleared');
  }

  // ═══════════════════════════════════════════════════════════════════
  // User Data Management
  // ═══════════════════════════════════════════════════════════════════

  /// บันทึกข้อมูลผู้ใช้ลง SecureStorage.
  ///
  /// [userData] — ข้อมูลผู้ใช้จาก AuthResponse.User (UserInfo)
  Future<void> setUserData(Map<String, dynamic> userData) async {
    await _storage.write(
      key: AppConstants.userDataKey,
      value: jsonEncode(userData),
    );
    _logger.d('👤 User data saved');
  }

  /// ดึงข้อมูลผู้ใช้จาก SecureStorage.
  ///
  /// Returns null หากไม่มีข้อมูล
  Future<Map<String, dynamic>?> getUserData() async {
    final data = await _storage.read(key: AppConstants.userDataKey);
    if (data == null) return null;
    try {
      return jsonDecode(data) as Map<String, dynamic>;
    } catch (e) {
      _logger.e('❌ Failed to parse user data', error: e);
      return null;
    }
  }

  // ═══════════════════════════════════════════════════════════════════
  // Private — Initialization
  // ═══════════════════════════════════════════════════════════════════

  /// ตรวจสอบ token ตอนเปิดแอป
  Future<void> _initializeAuth() async {
    final initializationVersion = _authMutationVersion;

    try {
      final token = await _storage.read(key: AppConstants.accessTokenKey);
      if (initializationVersion != _authMutationVersion) {
        _logger.d('Auth initialization superseded by a newer auth action');
        return;
      }

      if (token == null) {
        state = AuthStatus.unauthenticated;
        _logger.d('🔒 No token found');
        return;
      }

      // ตรวจสอบ token format validity ก่อน
      bool isExpired;
      try {
        isExpired = JwtDecoder.isExpired(token);
      } catch (e) {
        // Token malformed — ลบทิ้งเพื่อความปลอดภัย
        _logger.e('❌ Malformed token detected — clearing', error: e);
        await _storage.delete(key: AppConstants.accessTokenKey);
        if (initializationVersion != _authMutationVersion) return;
        state = AuthStatus.unauthenticated;
        return;
      }

      if (!isExpired) {
        _cachedToken = token;
        state = AuthStatus.authenticated;
        _startTokenClocking();
        _logger.i('🔓 Token found and valid — auto-login');
      } else {
        // Token หมดอายุ → ลองใช้ Refresh Token
        _logger.w('⏰ Access token expired — attempting refresh');

        if (initializationVersion != _authMutationVersion) return;
        final refreshed = await refreshAccessToken();
        if (!refreshed) {
          if (initializationVersion != _authMutationVersion) return;
          // Refresh ล้มเหลว → ลบ token เก่าออก
          await _storage.delete(key: AppConstants.accessTokenKey);
          if (initializationVersion != _authMutationVersion) return;
          state = AuthStatus.unauthenticated;
          _logger.w('⏰ Token refresh failed — cleared');
        }
        // ถ้า refresh สำเร็จ → state จะถูก set เป็น authenticated ใน setTokens()
      }
    } catch (e) {
      _logger.e('❌ Error during auth initialization', error: e);
      if (initializationVersion != _authMutationVersion) return;
      state = AuthStatus.unauthenticated;
    }
  }

  // ═══════════════════════════════════════════════════════════════════
  // Private — Token Clocking
  // ═══════════════════════════════════════════════════════════════════

  /// เริ่ม Token Clocking — ตรวจสอบ token expiry เป็นระยะ ๆ
  ///
  /// เทียบ Angular:
  /// ```typescript
  /// private startTokenClocking(): void {
  ///   this.clockingSubscription = interval(5000).subscribe(() => {
  ///     if (!this.hasValidToken() && this.isAuthenticated$.value) {
  ///       this.logout();
  ///     }
  ///   });
  /// }
  /// ```
  ///
  /// ปรับปรุง: ใช้ proactive refresh แทน direct logout
  /// — ตรวจสอบทุก 30 วินาที
  /// — หาก token เหลือ < 2 นาที → พยายาม refresh ก่อน
  /// — หาก refresh ล้มเหลว → logout
  void _startTokenClocking() {
    _stopTokenClocking();

    _clockingTimer = Timer.periodic(
      const Duration(seconds: 30),
      (_) => _onTokenClockTick(),
    );

    _logger.d('⏱️ Token clocking started (30s interval)');
  }

  /// หยุด Token Clocking.
  ///
  /// เทียบ Angular: `stopTokenClocking()`
  void _stopTokenClocking() {
    _clockingTimer?.cancel();
    _clockingTimer = null;
  }

  /// Handler สำหรับ Token Clocking tick.
  ///
  /// Logic:
  /// 1. ถ้าไม่มี token → ไม่ต้องทำอะไร (อาจจะ logout อยู่แล้ว)
  /// 2. ถ้า token expired → พยายาม refresh
  /// 3. ถ้า token กำลังจะหมดอายุ (< 2 นาที) → proactive refresh
  Future<void> _onTokenClockTick() async {
    if (_cachedToken == null) return;
    if (state != AuthStatus.authenticated) return;

    try {
      final token = _cachedToken!;
      final isExpired = JwtDecoder.isExpired(token);

      if (isExpired) {
        _logger.w('⏰ Token expired during use — attempting refresh');
        final refreshed = await refreshAccessToken();
        if (!refreshed) {
          _logger.w('⏰ Token refresh failed — forcing logout');
        }
        return;
      }

      // Proactive refresh: ตรวจสอบว่า token กำลังจะหมดอายุ
      final expiryDate = JwtDecoder.getExpirationDate(token);
      final remainingTime = expiryDate.difference(DateTime.now());

      if (remainingTime.inMinutes < 2) {
        _logger.i(
          '⏳ Token expiring soon (${remainingTime.inSeconds}s remaining) — proactive refresh',
        );
        await refreshAccessToken();
      }
    } catch (e) {
      _logger.e('❌ Error in token clock tick', error: e);
    }
  }

  // ═══════════════════════════════════════════════════════════════════
  // Private — Force Logout (ใช้เมื่อ refresh ล้มเหลว)
  // ═══════════════════════════════════════════════════════════════════

  /// Force logout — ลบ tokens ทั้งหมดโดยไม่ต้องเรียก API logout
  Future<void> _forceLogout() async {
    _authMutationVersion++;
    _stopTokenClocking();
    try {
      await ref.read(localDatabaseServiceProvider).clearAllData();
    } catch (_) {}
    try {
      ref.read(gpsBufferServiceProvider).clearBuffer();
    } catch (_) {}
    await _storage.delete(key: AppConstants.accessTokenKey);
    await _storage.delete(key: AppConstants.refreshTokenKey);
    await _storage.delete(key: AppConstants.userDataKey);
    _cachedToken = null;
    state = AuthStatus.unauthenticated;
    _logger.w('🔒 Force logout — all tokens cleared');
  }
}

final authServiceProvider = NotifierProvider<AuthService, AuthStatus>(
  AuthService.new,
);
