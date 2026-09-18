import 'dart:convert';
import 'package:logger/logger.dart';
import 'safe_storage.dart';
import '../config/app_constants.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

/// Manages secure storage for auth tokens and user data.
class AuthStorageService {
  final _storage = SafeStorage();

  Future<String?> readAccessToken() async {
    return await _storage.read(key: AppConstants.accessTokenKey);
  }

  Future<String?> readRefreshToken() async {
    return await _storage.read(key: AppConstants.refreshTokenKey);
  }

  Future<void> writeTokens({
    required String accessToken,
    required String refreshToken,
    Map<String, dynamic>? userData,
  }) async {
    await _storage.write(key: AppConstants.accessTokenKey, value: accessToken);
    await _storage.write(key: AppConstants.refreshTokenKey, value: refreshToken);
    if (userData != null) {
      await _storage.write(
        key: AppConstants.userDataKey,
        value: jsonEncode(userData),
      );
    }
  }

  Future<void> writeAccessTokenOnly(String token) async {
    await _storage.write(key: AppConstants.accessTokenKey, value: token);
  }

  Future<void> deleteAccessToken() async {
    await _storage.delete(key: AppConstants.accessTokenKey);
  }

  Future<void> clearAllAuthStorage() async {
    await _storage.delete(key: AppConstants.accessTokenKey);
    await _storage.delete(key: AppConstants.refreshTokenKey);
    await _storage.delete(key: AppConstants.userDataKey);
  }

  Future<void> writeUserData(Map<String, dynamic> userData) async {
    await _storage.write(
      key: AppConstants.userDataKey,
      value: jsonEncode(userData),
    );
    _logger.d('👤 User data saved');
  }

  Future<Map<String, dynamic>?> readUserData() async {
    final data = await _storage.read(key: AppConstants.userDataKey);
    if (data == null) return null;
    try {
      return jsonDecode(data) as Map<String, dynamic>;
    } catch (e) {
      _logger.e('❌ Failed to parse user data', error: e);
      return null;
    }
  }
}
