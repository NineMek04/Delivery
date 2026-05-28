import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../models/auth_response.dart';
import '../../config/app_constants.dart';
import '../api_helpers.dart';
import '../delivery_api_client.dart';

final authApiServiceProvider = Provider<AuthApiService>((ref) {
  return AuthApiService(ref.watch(deliveryApiClientProvider));
});

/// REST client for `/api/v1/auth/*`.
class AuthApiService {
  final Dio _dio;

  AuthApiService(this._dio);

  Future<AuthResponse> login({
    required String email,
    required String password,
  }) async {
    try {
      final response = await _dio.post(
        '${AppConstants.authEndpoint}/login',
        data: {'Email': email, 'Password': password},
      );
      final parsed = parseApiResponse(response.data, AuthResponse.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  Future<AuthResponse> register({
    required String email,
    required String password,
    required String fullName,
    required String role,
  }) async {
    try {
      final response = await _dio.post(
        '${AppConstants.authEndpoint}/register',
        data: {
          'Email': email,
          'Password': password,
          'FullName': fullName,
          'Role': role,
        },
      );
      final parsed = parseApiResponse(response.data, AuthResponse.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  Future<AuthResponse> refresh(String refreshToken) async {
    try {
      final response = await _dio.post(
        '${AppConstants.authEndpoint}/refresh',
        data: {'RefreshToken': refreshToken},
      );
      final parsed = parseApiResponse(response.data, AuthResponse.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  Future<void> logout() async {
    try {
      await _dio.post('${AppConstants.authEndpoint}/logout');
    } on DioException catch (e) {
      if (e.response?.statusCode != 401) {
        throw wrapDioError(e).error ?? e;
      }
    }
  }

  Future<UserInfo> getSession() async {
    try {
      final response = await _dio.get('${AppConstants.authEndpoint}/session');
      final parsed = parseApiResponse(response.data, UserInfo.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  Future<void> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {
    try {
      final response = await _dio.post(
        '${AppConstants.authEndpoint}/change-password',
        data: {
          'CurrentPassword': currentPassword,
          'NewPassword': newPassword,
        },
      );
      final parsed = parseApiResponse(response.data, (json) => null);
      ensureSuccess(parsed);
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }
}
