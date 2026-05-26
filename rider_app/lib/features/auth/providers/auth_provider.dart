import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/api/api_helpers.dart';
import '../../../core/api/services/auth_api_service.dart';
import '../../../core/api/services/notifications_api_service.dart';
import '../../../core/auth/auth_service.dart';

import 'package:flutter/foundation.dart';

final authNotifierProvider = NotifierProvider<AuthNotifier, AuthFormState>(
  AuthNotifier.new,
);

/// Auth form flow — login/logout via REST + AuthService token storage.
class AuthNotifier extends Notifier<AuthFormState> {
  @override
  AuthFormState build() {
    return const AuthFormState();
  }

  Future<void> login(String email, String password) async {
    debugPrint('[AuthNotifier] login started for $email');
    state = state.copyWith(isLoading: true, error: null);

    try {
      debugPrint('[AuthNotifier] Reading authApiServiceProvider...');
      final authApi = ref.read(authApiServiceProvider);
      debugPrint('[AuthNotifier] Calling authApi.login...');
      final response = await authApi.login(email: email, password: password);
      debugPrint('[AuthNotifier] authApi.login success! User: ${response.user.fullName}');

      debugPrint('[AuthNotifier] Saving tokens to authServiceProvider...');
      await ref.read(authServiceProvider.notifier).setTokens(
        accessToken: response.accessToken,
        refreshToken: response.refreshToken,
        userData: response.user.toJson(),
      );
      debugPrint('[AuthNotifier] Tokens saved successfully!');

      // ลงทะเบียนอุปกรณ์รับ Push Notification (Best-effort FCM Token Registration)
      try {
        final fcmToken = 'sim-token-rider-${response.user.id}';
        final deviceType = kIsWeb ? 'WEB' : (defaultTargetPlatform == TargetPlatform.iOS ? 'IOS' : 'ANDROID');
        
        debugPrint('[AuthNotifier] Registering simulated FCM token: $fcmToken for device: $deviceType');
        await ref.read(notificationsApiServiceProvider).registerFcmToken(
          token: fcmToken,
          deviceType: deviceType,
        );
        debugPrint('[AuthNotifier] FCM token registered successfully');
      } catch (fcmError) {
        debugPrint('[AuthNotifier] Failed to register FCM token (ignored for login flow): $fcmError');
      }

      state = state.copyWith(isLoading: false);
    } on ApiException catch (e) {
      debugPrint('[AuthNotifier] ApiException: ${e.message}');
      state = state.copyWith(isLoading: false, error: e.message);
    } catch (e, stack) {
      debugPrint('[AuthNotifier] Unexpected error: $e\n$stack');
      state = state.copyWith(isLoading: false, error: e.toString());
    }
  }

  Future<void> logout() async {
    state = state.copyWith(isLoading: true, error: null);

    try {
      final authApi = ref.read(authApiServiceProvider);
      await authApi.logout();
    } catch (_) {
      // Best-effort server logout.
    } finally {
      await ref.read(authServiceProvider.notifier).logout();
      state = state.copyWith(isLoading: false);
    }
  }
}

class AuthFormState {
  final bool isLoading;
  final String? error;

  const AuthFormState({
    this.isLoading = false,
    this.error,
  });

  AuthFormState copyWith({
    bool? isLoading,
    String? error,
  }) {
    return AuthFormState(
      isLoading: isLoading ?? this.isLoading,
      error: error,
    );
  }
}
