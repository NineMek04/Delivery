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
}
