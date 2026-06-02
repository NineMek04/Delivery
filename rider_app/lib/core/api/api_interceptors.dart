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
  final List<_PendingRequest> _queue = [];
  bool _isRefreshing = false;

  ErrorInterceptor(this._ref);

  @override
  void onError(DioException err, ErrorInterceptorHandler handler) async {
    final statusCode = err.response?.statusCode;

    if (statusCode == 401) {
      _logger.w('🔐 Unauthorized (401) — checking token refresh state');
      
      final requestPath = err.requestOptions.path;
      if (requestPath.contains('/auth/refresh') ||
          requestPath.contains('/auth/login')) {
        return handler.next(err);
      }

      if (_isRefreshing) {
        _logger.d('⏳ Token refresh in progress. Queueing request: ${err.requestOptions.uri}');
        _queue.add(_PendingRequest(err, handler));
        return;
      }

      _isRefreshing = true;
      _logger.i('🔑 Starting token refresh flow for request: ${err.requestOptions.uri}');

      try {
        final authService = _ref.read(authServiceProvider.notifier);
        final refreshed = await authService.refreshAccessToken();

        if (refreshed) {
          final newToken = authService.currentToken;
          if (newToken != null) {
            // Retry current request
            final response = await _retryRequest(err.requestOptions, newToken);
            handler.resolve(response);

            // Retry all queued requests
            _logger.i('🔄 Retrying ${_queue.length} queued requests with new token');
            for (final pending in _queue) {
              try {
                final queuedResponse = await _retryRequest(pending.err.requestOptions, newToken);
                pending.handler.resolve(queuedResponse);
              } catch (queuedErr) {
                if (queuedErr is DioException) {
                  pending.handler.reject(queuedErr);
                } else {
                  pending.handler.reject(
                    DioException(
                      requestOptions: pending.err.requestOptions,
                      error: queuedErr,
                    ),
                  );
                }
              }
            }
            _queue.clear();
            return;
          }
        }

        _logger.w('🔐 Token refresh failed — rejecting current and queued requests');
        handler.reject(err);
        for (final pending in _queue) {
          pending.handler.reject(pending.err);
        }
        _queue.clear();
      } catch (e) {
        _logger.e('❌ Unexpected error during token refresh orchestration', error: e);
        handler.reject(err);
        for (final pending in _queue) {
          pending.handler.reject(pending.err);
        }
        _queue.clear();
      } finally {
        _isRefreshing = false;
      }
      return;
    }

    switch (statusCode) {
      case 403:
        _logger.w('🚫 Forbidden (403) — Access denied');
        break;

      case 404:
        _logger.w('🔍 Not Found (404) — ${err.requestOptions.uri}');
        break;

      case 422:
        _logger.w('⚠️ Validation Error (422)');
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

  Future<Response> _retryRequest(RequestOptions opts, String token) async {
    opts.headers['Authorization'] = 'Bearer $token';

    final retryDio = Dio(
      BaseOptions(
        baseUrl: opts.baseUrl,
        connectTimeout: opts.connectTimeout,
        receiveTimeout: opts.receiveTimeout,
      ),
    );

    return await retryDio.request(
      opts.path,
      data: opts.data,
      queryParameters: opts.queryParameters,
      options: Options(
        method: opts.method,
        headers: opts.headers,
        responseType: opts.responseType,
      ),
    );
  }
}

class _PendingRequest {
  final DioException err;
  final ErrorInterceptorHandler handler;

  _PendingRequest(this.err, this.handler);
}
