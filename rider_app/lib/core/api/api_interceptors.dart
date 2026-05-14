import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:logger/logger.dart';

import '../auth/auth_service.dart';
import '../config/app_constants.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

/// Auth Interceptor — แนบ JWT Bearer token กับทุก request.
///
/// เทียบกับ:
/// - Angular: `admin-dashboard/src/app/core/interceptors/auth.interceptor.ts`
///
/// ```typescript
/// // Angular version:
/// req = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
/// ```
class AuthInterceptor extends Interceptor {
  final Ref _ref;

  AuthInterceptor(this._ref);

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    final authService = _ref.read(authServiceProvider);
    final token = authService.currentToken;

    if (token != null && token.isNotEmpty) {
      options.headers['Authorization'] = 'Bearer $token';
    }

    handler.next(options);
  }
}

/// Error Interceptor — จัดการ Global HTTP Errors.
///
/// เทียบกับ:
/// - Angular: `admin-dashboard/src/app/core/interceptors/error.interceptor.ts`
///
/// จัดการ:
/// - 401 → Token expired / ไม่ได้ login → redirect to login
/// - 403 → ไม่มีสิทธิ์เข้าถึง
/// - 500 → Server error
/// - Network error → ไม่มีอินเทอร์เน็ต
class ErrorInterceptor extends Interceptor {
  ErrorInterceptor();

  @override
  void onError(DioException err, ErrorInterceptorHandler handler) {
    final statusCode = err.response?.statusCode;

    switch (statusCode) {
      case 401:
        _logger.w('🔐 Unauthorized (401) — Token expired or invalid');
        // TODO: Trigger logout / redirect to login via AuthService
        // เทียบ Angular: this.router.navigate(['/auth/login']);
        break;

      case 403:
        _logger.w('🚫 Forbidden (403) — Access denied');
        // TODO: Show access denied dialog
        break;

      case 404:
        _logger.w('🔍 Not Found (404) — ${err.requestOptions.uri}');
        break;

      case 422:
        _logger.w('⚠️ Validation Error (422)');
        // Validation errors จาก BackendApi's ValidationFilter
        break;

      case 500:
        _logger.e('💥 Server Error (500)', error: err.message);
        // TODO: Show server error dialog (เทียบ SweetAlert2 ใน Angular)
        break;

      default:
        if (err.type == DioExceptionType.connectionTimeout ||
            err.type == DioExceptionType.receiveTimeout) {
          _logger.e('⏱️ Timeout — ${err.requestOptions.uri}');
        } else if (err.type == DioExceptionType.connectionError) {
          _logger.e('📡 No internet connection');
        }
    }

    handler.next(err);
  }
}
