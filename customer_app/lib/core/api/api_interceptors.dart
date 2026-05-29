import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:logger/logger.dart';

import '../auth/auth_service.dart';

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
    final authService = _ref.read(authServiceProvider.notifier);
    final token = authService.currentToken;

    if (token != null && token.isNotEmpty) {
      options.headers['Authorization'] = 'Bearer $token';
    }

    handler.next(options);
  }
}

/// Error Interceptor — จัดการ Global HTTP Errors + Auto Refresh Token.
///
/// เทียบกับ:
/// - Angular: `admin-dashboard/src/app/core/interceptors/error.interceptor.ts`
///
/// จัดการ:
/// - 401 → Token expired → พยายาม refresh แล้ว retry request
/// - 403 → ไม่มีสิทธิ์เข้าถึง
/// - 500 → Server error
/// - Network error → ไม่มีอินเทอร์เน็ต
class ErrorInterceptor extends Interceptor {
  final Ref _ref;

  ErrorInterceptor(this._ref);

  @override
  void onError(DioException err, ErrorInterceptorHandler handler) async {
    final statusCode = err.response?.statusCode;

    switch (statusCode) {
      case 401:
        _logger.w('🔐 Unauthorized (401) — attempting token refresh');
        // เทียบ Angular: ไม่มี auto-refresh แต่เราเพิ่มให้ดีกว่า
        // ลอง refresh token แล้ว retry request เดิม
        final refreshed = await _tryRefreshAndRetry(err, handler);
        if (refreshed) return; // retry สำเร็จ → ไม่ต้อง propagate error

        // Refresh ล้มเหลว → logout (AuthService จัดการแล้ว)
        _logger.w('🔐 Token refresh failed — user will be redirected to login');
        break;

      case 403:
        _logger.w('🚫 Forbidden (403) — Access denied');
        break;

      case 404:
        _logger.w('🔍 Not Found (404) — ${err.requestOptions.uri}');
        break;

      case 422:
        _logger.w('⚠️ Validation Error (422)');
        // Validation errors จาก BackendApi's ValidationFilter
        break;

      case 429:
        _logger.w('⏱️ Rate Limited (429) — Too many requests');
        break;

      case 500:
        _logger.e('💥 Server Error (500)', error: err.message);
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

  /// พยายาม refresh token แล้ว retry request เดิม.
  ///
  /// Returns true หาก retry สำเร็จ (handler.resolve ถูกเรียกแล้ว)
  /// Returns false หาก refresh หรือ retry ล้มเหลว
  Future<bool> _tryRefreshAndRetry(
    DioException err,
    ErrorInterceptorHandler handler,
  ) async {
    // ป้องกันไม่ให้ refresh request ตัวเอง retry วนลูป
    final requestPath = err.requestOptions.path;
    if (requestPath.contains('/auth/refresh') ||
        requestPath.contains('/auth/login')) {
      return false;
    }

    try {
      final authService = _ref.read(authServiceProvider.notifier);
      final refreshed = await authService.refreshAccessToken();

      if (!refreshed) return false;

      // Retry original request ด้วย token ใหม่
      final newToken = authService.currentToken;
      if (newToken == null) return false;

      final opts = err.requestOptions;
      opts.headers['Authorization'] = 'Bearer $newToken';

      // สร้าง Dio instance ใหม่สำหรับ retry (ป้องกัน interceptor loop)
      final retryDio = Dio(
        BaseOptions(
          baseUrl: opts.baseUrl,
          connectTimeout: opts.connectTimeout,
          receiveTimeout: opts.receiveTimeout,
        ),
      );

      final response = await retryDio.request(
        opts.path,
        data: opts.data,
        queryParameters: opts.queryParameters,
        options: Options(
          method: opts.method,
          headers: opts.headers,
          responseType: opts.responseType,
        ),
      );

      handler.resolve(response);
      _logger.i('🔄 Request retried successfully after token refresh');
      return true;
    } catch (e) {
      _logger.e('❌ Retry after refresh failed', error: e);
      return false;
    }
  }
}
