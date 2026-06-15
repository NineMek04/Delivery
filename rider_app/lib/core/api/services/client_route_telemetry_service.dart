import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../delivery_api_client.dart';

final clientRouteTelemetryServiceProvider =
    Provider<ClientRouteTelemetryService>((ref) {
  return ClientRouteTelemetryService(ref.watch(deliveryApiClientProvider));
});

class ClientRouteTelemetryService {
  ClientRouteTelemetryService(this._dio);

  final Dio _dio;

  Future<void> reportFallback({
    required String orderId,
    required String routePhase,
    required String reason,
    int? encodedLength,
  }) async {
    try {
      await _dio.post(
        'telemetry/client-route-fallback',
        data: {
          'orderId': orderId,
          'routePhase': routePhase,
          'reason': reason,
          'encodedLength': encodedLength,
        },
      );
    } catch (_) {
      // Diagnostics must never interrupt active delivery navigation.
    }
  }
}
