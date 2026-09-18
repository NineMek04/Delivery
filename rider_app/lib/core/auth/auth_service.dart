import 'dart:async';
import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:jwt_decoder/jwt_decoder.dart';
import 'package:logger/logger.dart';
import '../database/local_database_service.dart';
import '../../models/auth_response.dart';
import '../api/delivery_api_client.dart';
import 'auth_status.dart';
import 'auth_storage_service.dart';
import 'auth_claims_helper.dart';
import 'auth_token_refresher.dart';
import '../location/gps_buffer_service.dart';

export 'auth_status.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

/// AuthService — จัดการ JWT token lifecycle + Refresh Token + Token Clocking.
class AuthService extends Notifier<AuthStatus> {
  final _storage = AuthStorageService();

  /// Cached token เพื่อไม่ต้องอ่าน storage ทุกครั้ง
  String? _cachedToken;

  /// Timer สำหรับ Token Clocking
  Timer? _clockingTimer;

  /// Future สำหรับป้องกัน concurrent refresh และแชร์ผลลัพธ์ร่วมกัน
  Future<bool>? _refreshFuture;

  /// Prevents a stale async startup check from overwriting a newer login/logout.
  int _authMutationVersion = 0;

  @override
  AuthStatus build() {
    ref.onDispose(() {
      _stopTokenClocking();
    });

    _initializeAuth();
    return AuthStatus.loading;
  }

  // ═══════════════════════════════════════════════════════════════════
  // Token Access (Sync & Async)
  // ═══════════════════════════════════════════════════════════════════

  String? get currentToken => _cachedToken;

  bool get isTokenValid {
    if (_cachedToken == null) return false;
    try {
      return !JwtDecoder.isExpired(_cachedToken!);
    } catch (e) {
      _logger.e('❌ Invalid token format', error: e);
      return false;
    }
  }

  Future<String?> getValidToken() async {
    if (_cachedToken != null && isTokenValid) {
      return _cachedToken;
    }

    try {
      final token = await _storage.readAccessToken();
      if (token != null && token.isNotEmpty) {
        _cachedToken = token;
        if (isTokenValid) {
          return token;
        }
      }

      final refreshed = await refreshAccessToken();
      if (refreshed && _cachedToken != null) {
        return _cachedToken;
      }
    } catch (e) {
      _logger.w('⚠️ Error getting valid token: $e');
    }

    return _cachedToken;
  }

  // ═══════════════════════════════════════════════════════════════════
  // JWT Decode & Claims
  // ═══════════════════════════════════════════════════════════════════

  Map<String, dynamic>? get decodedToken => AuthClaimsHelper.decodeToken(_cachedToken);
  String? get userId => AuthClaimsHelper.getUserId(decodedToken);
  String? get userName => AuthClaimsHelper.getUserName(decodedToken);
  String? get userRole => AuthClaimsHelper.getUserRole(decodedToken);
  String? get userEmail => AuthClaimsHelper.getUserEmail(decodedToken);
  UserInfo? get currentUser => AuthClaimsHelper.getCurrentUser(decodedToken);

  // ═══════════════════════════════════════════════════════════════════
  // Token Lifecycle — Set / Refresh / Logout
  // ═══════════════════════════════════════════════════════════════════

  Future<void> setTokens({
    required String accessToken,
    required String refreshToken,
    Map<String, dynamic>? userData,
  }) async {
    _authMutationVersion++;
    await _storage.writeTokens(
      accessToken: accessToken,
      refreshToken: refreshToken,
      userData: userData,
    );

    _cachedToken = accessToken;
    state = AuthStatus.authenticated;
    _startTokenClocking();
    _logger.i('🔑 Tokens saved — access + refresh');
  }

  Future<void> setToken(String token) async {
    _authMutationVersion++;
    await _storage.writeAccessTokenOnly(token);
    _cachedToken = token;
    state = AuthStatus.authenticated;
    _startTokenClocking();
    _logger.i('🔑 Token saved');
  }

  Future<bool> refreshAccessToken() async {
    if (_refreshFuture != null) {
      _logger.d('⏳ Token refresh already in progress, awaiting current refresh future...');
      return await _refreshFuture!;
    }

    final refreshVersion = _authMutationVersion;
    final completer = Completer<bool>();
    _refreshFuture = completer.future;

    try {
      final refreshToken = await _storage.readRefreshToken();
      if (refreshToken == null || refreshToken.isEmpty) {
        _logger.w('⚠️ No refresh token found — forcing re-login');
        await _forceLogout();
        completer.complete(false);
        return false;
      }

      final baseUrl = ref.read(deliveryApiClientProvider).options.baseUrl;
      final auth = await AuthTokenRefresher.requestRefresh(
        baseUrl: baseUrl,
        refreshToken: refreshToken,
      );

      if (auth != null) {
        if (refreshVersion != _authMutationVersion) {
          _logger.d('Token refresh superseded by a newer auth action');
          completer.complete(false);
          return false;
        }

        await setTokens(
          accessToken: auth.accessToken,
          refreshToken: auth.refreshToken,
          userData: auth.user.toJson(),
        );

        _logger.i('🔄 Token refreshed successfully');
        completer.complete(true);
        return true;
      } else {
        if (refreshVersion == _authMutationVersion) {
          await _forceLogout();
        }
        completer.complete(false);
        return false;
      }
    } on DioException catch (e) {
      _logger.e('❌ Token refresh failed (${e.response?.statusCode})', error: e.message);
      final statusCode = e.response?.statusCode;
      final credentialsRejected = statusCode == 400 || statusCode == 401 || statusCode == 403;
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

  Future<void> logout() async {
    _authMutationVersion++;
    _stopTokenClocking();
    try {
      await ref.read(localDatabaseServiceProvider).clearAllData();
    } catch (_) {}
    try {
      ref.read(gpsBufferServiceProvider).clearBuffer();
    } catch (_) {}
    await _storage.clearAllAuthStorage();
    _cachedToken = null;
    state = AuthStatus.unauthenticated;
    _logger.i('🔒 Logged out — tokens cleared');
  }

  // ═══════════════════════════════════════════════════════════════════
  // User Data Management
  // ═══════════════════════════════════════════════════════════════════

  Future<void> setUserData(Map<String, dynamic> userData) async {
    await _storage.writeUserData(userData);
  }

  Future<Map<String, dynamic>?> getUserData() async {
    return await _storage.readUserData();
  }

  // ═══════════════════════════════════════════════════════════════════
  // Private — Initialization & Clocking
  // ═══════════════════════════════════════════════════════════════════

  Future<void> _initializeAuth() async {
    final initializationVersion = _authMutationVersion;

    try {
      final token = await _storage.readAccessToken();
      if (initializationVersion != _authMutationVersion) return;

      if (token == null) {
        state = AuthStatus.unauthenticated;
        _logger.d('🔒 No token found');
        return;
      }

      bool isExpired;
      try {
        isExpired = JwtDecoder.isExpired(token);
      } catch (e) {
        _logger.e('❌ Malformed token detected — clearing', error: e);
        await _storage.deleteAccessToken();
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
        _logger.w('⏰ Access token expired — attempting refresh');
        if (initializationVersion != _authMutationVersion) return;
        final refreshed = await refreshAccessToken();
        if (!refreshed) {
          if (initializationVersion != _authMutationVersion) return;
          await _storage.deleteAccessToken();
          if (initializationVersion != _authMutationVersion) return;
          state = AuthStatus.unauthenticated;
          _logger.w('⏰ Token refresh failed — cleared');
        }
      }
    } catch (e) {
      _logger.e('❌ Error during auth initialization', error: e);
      if (initializationVersion != _authMutationVersion) return;
      state = AuthStatus.unauthenticated;
    }
  }

  void _startTokenClocking() {
    _stopTokenClocking();
    _clockingTimer = Timer.periodic(
      const Duration(seconds: 30),
      (_) => _onTokenClockTick(),
    );
    _logger.d('⏱️ Token clocking started (30s interval)');
  }

  void _stopTokenClocking() {
    _clockingTimer?.cancel();
    _clockingTimer = null;
  }

  Future<void> _onTokenClockTick() async {
    if (_cachedToken == null || state != AuthStatus.authenticated) return;

    try {
      final token = _cachedToken!;
      if (JwtDecoder.isExpired(token)) {
        _logger.w('⏰ Token expired during use — attempting refresh');
        final refreshed = await refreshAccessToken();
        if (!refreshed) {
          _logger.w('⏰ Token refresh failed — forcing logout');
        }
        return;
      }

      final expiryDate = JwtDecoder.getExpirationDate(token);
      final remainingTime = expiryDate.difference(DateTime.now());
      if (remainingTime.inMinutes < 2) {
        _logger.i('⏳ Token expiring soon (${remainingTime.inSeconds}s remaining) — proactive refresh');
        await refreshAccessToken();
      }
    } catch (e) {
      _logger.e('❌ Error in token clock tick', error: e);
    }
  }

  Future<void> _forceLogout() async {
    _authMutationVersion++;
    _stopTokenClocking();
    try {
      await ref.read(localDatabaseServiceProvider).clearAllData();
    } catch (_) {}
    try {
      ref.read(gpsBufferServiceProvider).clearBuffer();
    } catch (_) {}
    await _storage.clearAllAuthStorage();
    _cachedToken = null;
    state = AuthStatus.unauthenticated;
    _logger.w('🔒 Force logout — all tokens cleared');
  }
}

final authServiceProvider = NotifierProvider<AuthService, AuthStatus>(
  AuthService.new,
);
