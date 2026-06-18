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
  static const Set<String> _allowedReasons = {
    'MISSING_POLYLINE',
    'INVALID_POLYLINE',
    'LOCAL_OSRM_UNAVAILABLE',
  };
  static const Set<String> _allowedRoutePhases = {
    'PICKUP',
    'DELIVERY',
  };

  String _normalizeReason(String reason) {
    final normalized = reason.trim().toUpperCase();
    if (_allowedReasons.contains(normalized)) return normalized;
    return 'INVALID_POLYLINE';
  }

  Future<void> reportFallback({
    required String orderId,
    required String routePhase,
    required String reason,
    int? encodedLength,
  }) async {
    final normalizedPhase = routePhase.trim().toUpperCase();
    if (orderId.trim().isEmpty || !_allowedRoutePhases.contains(normalizedPhase)) {
      return;
    }

    try {
      await _dio.post(
        'telemetry/client-route-fallback',
        data: {
          'orderId': orderId.trim(),
          'routePhase': normalizedPhase,
          'reason': _normalizeReason(reason),
          'encodedLength': encodedLength,
        },
      );
    } catch (_) {
      // Diagnostics must never interrupt active delivery navigation.
    }
  }
}
