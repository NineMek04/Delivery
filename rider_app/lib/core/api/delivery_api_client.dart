import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../config/environment.dart';
import 'api_interceptors.dart';

part 'delivery_api_client.g.dart';

/// Dio-based HTTP client for communicating with BackendApi.
///
/// เทียบกับ:
/// - Angular: `admin-dashboard/src/app/core/http/delivery-http-request.ts`
///
/// แนวคิด:
/// - Angular ใช้ Fluent API (`req<T>('path').body(data).post()`)
/// - Flutter ใช้ Dio instance + interceptors ซึ่งทำหน้าที่เดียวกัน
///
/// Usage:
/// ```dart
/// final dio = ref.watch(deliveryApiClientProvider);
/// final response = await dio.get('/riders');
/// ```
@riverpod
Dio deliveryApiClient(Ref ref) {
  final dio = Dio(
    BaseOptions(
      baseUrl: Environment.apiUrl,
      connectTimeout: Environment.connectTimeout,
      receiveTimeout: Environment.receiveTimeout,
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'application/json',
      },
    ),
  );

  // ── Interceptors (เทียบ Angular interceptors) ───────────────────
  dio.interceptors.addAll([
    // 1. Auth interceptor — แนบ Bearer token (เทียบ auth.interceptor.ts)
    AuthInterceptor(ref),

    // 2. Error interceptor — จัดการ global errors (เทียบ error.interceptor.ts)
    ErrorInterceptor(),

    // 3. Logging — เฉพาะ dev mode
    if (Environment.enableHttpLogging)
      LogInterceptor(
        requestBody: true,
        responseBody: true,
        requestHeader: false,
        responseHeader: false,
      ),
  ]);

  return dio;
}
