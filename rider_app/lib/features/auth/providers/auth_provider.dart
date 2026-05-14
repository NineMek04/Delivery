import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../../../core/auth/auth_service.dart';

part 'auth_provider.g.dart';

/// Auth Provider — จัดการ state ของ authentication flow.
///
/// เทียบกับ:
/// - Angular: `AuthService` + component-level state
/// - .NET: `AuthController` (server-side)
///
/// TODO: Implement login/register methods ที่เรียก BackendApi
@riverpod
class AuthNotifier extends _$AuthNotifier {
  @override
  AuthFormState build() {
    return const AuthFormState();
  }

  /// Login ด้วย email + password.
  ///
  /// Flow: Flutter → BackendApi AuthController → JWT token → save via AuthService
  Future<void> login(String email, String password) async {
    state = state.copyWith(isLoading: true, error: null);

    try {
      // TODO: Call BackendApi login endpoint
      // final response = await dio.post('/auth/login', data: {...});
      // final token = response.data['Value']['token'];
      // await ref.read(authServiceProvider.notifier).setToken(token);

      state = state.copyWith(isLoading: false);
    } catch (e) {
      state = state.copyWith(isLoading: false, error: e.toString());
    }
  }
}

/// Auth form state.
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
