import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../config/environment.dart';
import 'api_interceptors.dart';
import 'web_url_resolver_stub.dart'
    if (dart.library.html) 'web_url_resolver_web.dart';

/// Resolves the absolute base URL for Dio.
///
/// Docker Web (same-origin):
///   API_BASE_URL is empty → reads window.location.origin at runtime
///   → "http://localhost:8080/api/v1"  (proxied by nginx to backend)
///
/// Native dev (Android Emulator / LAN):
///   --dart-define=API_BASE_URL=http://10.0.2.2:5000
///   → "http://10.0.2.2:5000/api/v1"
String _resolveBaseUrl() {
  String url;

  // Explicit base URL set via --dart-define (native dev).
  if (Environment.apiBaseUrl.isNotEmpty) {
    url = Environment.apiUrl;
  } else if (kIsWeb) {
    // Flutter Web in Docker: build absolute URL from the browser's own origin.
    final origin = getWindowOrigin(); // e.g. "http://localhost:8080"
    if (origin.isNotEmpty) {
      url = '$origin${Environment.apiPrefix}';
    } else {
      url = Environment.apiUrl;
    }
  } else {
    url = Environment.apiUrl;
  }

  // Ensure the base URL ends with a slash so Dio handles relative paths correctly
  if (!url.endsWith('/')) {
    url = '$url/';
  }

  return url;
}

/// Dio-based HTTP client for communicating with BackendApi.
final deliveryApiClientProvider = Provider<Dio>((ref) {
  final baseUrl = _resolveBaseUrl();

  final dio = Dio(
    BaseOptions(
      baseUrl: baseUrl,
      connectTimeout: Environment.connectTimeout,
      receiveTimeout: Environment.receiveTimeout,
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'application/json',
      },
    ),
  );

  dio.interceptors.addAll([
    AuthInterceptor(ref),
    ErrorInterceptor(ref),
    if (Environment.enableHttpLogging)
      LogInterceptor(
        requestBody: true,
        responseBody: true,
        requestHeader: false,
        responseHeader: false,
      ),
  ]);

  return dio;
});
