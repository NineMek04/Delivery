import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../api_helpers.dart';
import '../delivery_api_client.dart';

final notificationsApiServiceProvider = Provider<NotificationsApiService>((ref) {
  return NotificationsApiService(ref.watch(deliveryApiClientProvider));
});

/// REST client for `/api/v1/notifications/*`.
class NotificationsApiService {
  final Dio _dio;

  NotificationsApiService(this._dio);

  /// ลงทะเบียนหรืออัปเดต FCM Token สำหรับอุปกรณ์ไรเดอร์
  Future<void> registerFcmToken({
    required String token,
    String? deviceType,
  }) async {
    try {
      final response = await _dio.post(
        'notifications/register-token',
        data: {
          'Token': token,
          if (deviceType != null) 'DeviceType': deviceType,
        },
      );
      final parsed = parseApiResponse(response.data, (json) => null);
      ensureSuccess(parsed);
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }
}
