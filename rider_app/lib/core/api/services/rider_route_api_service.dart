import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:latlong2/latlong.dart';

import '../api_helpers.dart';
import '../delivery_api_client.dart';

final riderRouteApiServiceProvider = Provider<RiderRouteApiService>((ref) {
  return RiderRouteApiService(ref.watch(deliveryApiClientProvider));
});

class RiderRouteApiService {
  RiderRouteApiService(this._dio);

  final Dio _dio;

  Future<RiderResolvedRoute> resolve({
    required String orderId,
    required String routePhase,
    required double currentLat,
    required double currentLng,
  }) async {
    try {
      final response = await _dio.post(
        'rider-routes/resolve',
        data: {
          'orderId': orderId,
          'routePhase': routePhase,
          'currentLat': currentLat,
          'currentLng': currentLng,
        },
      );
      final parsed = parseApiResponse(
        response.data,
        RiderResolvedRoute.fromJson,
      );
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (error) {
      throw wrapDioError(error).error ?? error;
    }
  }
}

class RiderResolvedRoute {
  const RiderResolvedRoute({
    required this.encodedPolyline,
    required this.coordinates,
    required this.distanceMeters,
    required this.durationSeconds,
    required this.source,
  });

  final String encodedPolyline;
  final List<LatLng> coordinates;
  final double distanceMeters;
  final double durationSeconds;
  final String source;

  factory RiderResolvedRoute.fromJson(Map<String, dynamic> json) {
    final rawCoordinates =
        readField<List>(json, 'coordinates') ??
        readField<List>(json, 'Coordinates') ??
        const [];

    return RiderResolvedRoute(
      encodedPolyline:
          readField<String>(json, 'encodedPolyline') ??
          readField<String>(json, 'EncodedPolyline') ??
          '',
      coordinates: _readCoordinates(rawCoordinates),
      distanceMeters:
          (readField<num>(json, 'distanceMeters') ??
                  readField<num>(json, 'DistanceMeters') ??
                  0)
              .toDouble(),
      durationSeconds:
          (readField<num>(json, 'durationSeconds') ??
                  readField<num>(json, 'DurationSeconds') ??
                  0)
              .toDouble(),
      source:
          readField<String>(json, 'source') ??
          readField<String>(json, 'Source') ??
          'HAVERSINE_FALLBACK',
    );
  }

  static List<LatLng> _readCoordinates(List rawCoordinates) {
    final points = <LatLng>[];
    for (final raw in rawCoordinates) {
      if (raw is List && raw.length >= 2) {
        final lng = _toDouble(raw[0]);
        final lat = _toDouble(raw[1]);
        if (lat != null &&
            lng != null &&
            lat >= -90 &&
            lat <= 90 &&
            lng >= -180 &&
            lng <= 180) {
          points.add(LatLng(lat, lng));
        }
      }
    }
    return points;
  }

  static double? _toDouble(dynamic value) {
    if (value is num) return value.toDouble();
    if (value is String) return double.tryParse(value);
    return null;
  }
}
