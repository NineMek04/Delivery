import 'package:dio/dio.dart';
import 'package:logger/logger.dart';
import '../../models/auth_response.dart';
import '../api/api_helpers.dart';
import '../config/app_constants.dart';
import '../config/environment.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

class AuthTokenRefresher {
  /// Calls the backend refresh endpoint with [refreshToken].
  /// Returns [AuthResponse] on success, throws [DioException] on failure, or null on business error.
  static Future<AuthResponse?> requestRefresh({
    required String baseUrl,
    required String refreshToken,
  }) async {
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
      return parsed.value!;
    } else {
      _logger.w('⚠️ Refresh API returned failure: ${parsed.message}');
      return null;
    }
  }
}
