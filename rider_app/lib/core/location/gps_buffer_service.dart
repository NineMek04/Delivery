import 'dart:async';
import 'dart:math';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';
import 'package:logger/logger.dart';

import '../api/delivery_api_client.dart';
import '../database/local_database_service.dart';
import '../config/app_constants.dart';

final gpsBufferServiceProvider = Provider<GpsBufferService>((ref) {
  return GpsBufferService(
    dio: ref.watch(deliveryApiClientProvider),
    db: ref.watch(localDatabaseServiceProvider),
  );
});

/// GPS Buffer & Batch Ingestion Service
///
/// ใช้ in-memory buffer ทั้งบน Web และ Mobile เพื่อความเรียบง่ายและ
/// compatibility กับ flutter web build
///
/// Data Flow:
/// LocationService → GpsBufferService (in-memory) → POST /api/v1/telemetry/gps/batch
class GpsBufferService {
  final Dio _dio;
  final LocalDatabaseService _db;
  final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

  Timer? _syncTimer;
  int _syncIntervalSeconds = 5;
  bool _isSyncing = false;
  bool _isSyncingStatus = false;

  // In-memory GPS point buffer (used on both Web and Mobile)
  final List<Map<String, dynamic>> _gpsPoints = [];

  // Adaptive sampling state
  double? _lastBufferedLat;
  double? _lastBufferedLng;
  double? _lastBufferedHeading;

  GpsBufferService({
    required Dio dio,
    required LocalDatabaseService db,
  })  : _dio = dio,
        _db = db;

  /// Starts the background periodic sync scheduler.
  void startSyncTimer() {
    _syncTimer?.cancel();
    _logger.i('🛰️ Starting GPS sync timer (every $_syncIntervalSeconds seconds)');
    _syncTimer = Timer.periodic(Duration(seconds: _syncIntervalSeconds), (_) {
      syncBufferedPoints();
      syncPendingStatusUpdates();
    });
  }

  /// Stops the periodic sync timer.
  void stopSyncTimer() {
    _syncTimer?.cancel();
    _syncTimer = null;
    _logger.i('🛑 Stopped GPS sync timer');
  }

  /// Adjusts sync frequency based on backend backpressure header.
  void updateSyncInterval(int intervalSeconds) {
    if (intervalSeconds < 3) intervalSeconds = 3;
    if (_syncIntervalSeconds != intervalSeconds) {
      _logger.i('🔄 Adjusting sync interval: $_syncIntervalSeconds → $intervalSeconds seconds');
      _syncIntervalSeconds = intervalSeconds;
      if (_syncTimer != null) startSyncTimer();
    }
  }

  /// Buffers a GPS coordinate with adaptive sampling.
  Future<void> bufferLocation(
    double latitude,
    double longitude,
    double accuracy, {
    double? heading,
  }) async {
    try {
      // Adaptive sampling — skip point if not moved enough
      if (_lastBufferedLat != null && _lastBufferedLng != null) {
        final distance = Geolocator.distanceBetween(
          _lastBufferedLat!,
          _lastBufferedLng!,
          latitude,
          longitude,
        );

        bool shouldBuffer = distance >= 15.0;

        if (!shouldBuffer && heading != null && _lastBufferedHeading != null) {
          final diff = (heading - _lastBufferedHeading!).abs();
          final normalized = diff > 180 ? 360 - diff : diff;
          if (normalized >= 15.0) shouldBuffer = true;
        } else if (!shouldBuffer && heading != null) {
          shouldBuffer = true;
        }

        if (!shouldBuffer) {
          _logger.d('📍 GPS skipped (adaptive sampling): dist=${distance.toStringAsFixed(1)}m');
          return;
        }
      }

      _lastBufferedLat = latitude;
      _lastBufferedLng = longitude;
      if (heading != null) _lastBufferedHeading = heading;

      _gpsPoints.add({
        'id': DateTime.now().millisecondsSinceEpoch + Random().nextInt(1000),
        'latitude': latitude,
        'longitude': longitude,
        'accuracy': accuracy,
        'timestamp': DateTime.now().toUtc().toIso8601String(),
      });

      // Cap buffer at 10,000 points (FIFO)
      if (_gpsPoints.length >= 10000) {
        _gpsPoints.removeRange(0, _gpsPoints.length - 10000 + 1);
        _logger.w('⚠️ GPS buffer limit (10,000) reached — purged oldest points');
      }

      _logger.d('📍 Buffered GPS (${kIsWeb ? "web" : "mobile"}): ($latitude, $longitude)');

      // Proactive sync when buffer builds up
      if (_gpsPoints.length >= 10 && !_isSyncing) {
        syncBufferedPoints();
      }
    } catch (e) {
      _logger.e('❌ Failed to buffer GPS point', error: e);
    }
  }

  /// Syncs buffered GPS points to the backend in batches of 100.
  Future<void> syncBufferedPoints() async {
    if (_isSyncing || _gpsPoints.isEmpty) return;
    _isSyncing = true;

    try {
      final batch = _gpsPoints.take(100).toList();
      final batchIds = batch.map((p) => p['id'] as int).toList();

      _logger.d('📡 Syncing ${batch.length} GPS points to backend...');

      final payload = batch.map((p) => {
        'Latitude': p['latitude'],
        'Longitude': p['longitude'],
        'Accuracy': p['accuracy'],
        'Timestamp': p['timestamp'],
      }).toList();

      final response = await _dio.post('telemetry/gps/batch', data: payload);

      // Respect backpressure recommendation from backend
      final pingHeader = response.headers.value('X-Recommended-Ping');
      if (pingHeader != null) {
        final newInterval = int.tryParse(pingHeader);
        if (newInterval != null) updateSyncInterval(newInterval);
      }

      if (response.statusCode == 200) {
        _logger.i('✅ Batch upload of ${batch.length} GPS points succeeded');
        final toRemove = batchIds.toSet();
        _gpsPoints.removeWhere((p) => toRemove.contains(p['id']));

        // Chain next batch if more remain
        if (_gpsPoints.isNotEmpty) {
          final jitter = 500 + Random().nextInt(1500);
          Future.delayed(Duration(milliseconds: jitter), syncBufferedPoints);
        }
      } else if (response.statusCode == 429) {
        _logger.w('⚠️ Batch throttled (429) — keeping points');
      } else {
        _logger.w('⚠️ Batch upload returned ${response.statusCode} — keeping points');
      }
    } on DioException catch (e) {
      if (e.response?.statusCode == 429) {
        _logger.w('⚠️ Batch throttled via exception (429)');
      } else {
        _logger.w('🔌 Network error during GPS batch upload: ${e.message}');
      }
      final pingHeader = e.response?.headers.value('X-Recommended-Ping');
      if (pingHeader != null) {
        final newInterval = int.tryParse(pingHeader);
        if (newInterval != null) updateSyncInterval(newInterval);
      }
    } catch (e) {
      _logger.e('❌ Unexpected error during GPS batch sync', error: e);
    } finally {
      _isSyncing = false;
    }
  }

  /// Syncs pending offline order status updates stored in SQLite.
  Future<void> syncPendingStatusUpdates() async {
    if (_isSyncingStatus) return;
    _isSyncingStatus = true;

    try {
      final pendingList = await _db.getPendingStatusUpdates();
      if (pendingList.isEmpty) return;

      _logger.d('📡 Syncing ${pendingList.length} pending order status updates...');

      for (final update in pendingList) {
        final id = update['id'] as int;
        final orderId = update['order_id'] as String;
        final status = update['status'] as String;

        try {
          final response = await _dio.patch(
            '${AppConstants.ordersEndpoint}/$orderId/status',
            data: {'Status': status},
          );

          if (response.statusCode == 200 || response.statusCode == 204) {
            _logger.i('✅ Synced status update: orderId=$orderId → $status');
            await _db.deletePendingStatusUpdate(id);
          } else if (response.statusCode != null &&
              response.statusCode! >= 400 &&
              response.statusCode! < 500) {
            _logger.e('❌ 4xx error for orderId=$orderId — dropping update');
            await _db.saveLocalErrorLog(
              'PATCH ${AppConstants.ordersEndpoint}/$orderId/status',
              'Client Error ${response.statusCode}: ${response.statusMessage}',
              '{"Status": "$status"}',
            );
            await _db.deletePendingStatusUpdate(id);
          } else {
            _logger.w('⚠️ Status update failed (${response.statusCode}) — will retry');
            break;
          }
        } on DioException catch (dioErr) {
          final code = dioErr.response?.statusCode;
          if (code != null && code >= 400 && code < 500) {
            _logger.e('❌ DioException 4xx for orderId=$orderId — dropping update');
            await _db.saveLocalErrorLog(
              'PATCH ${AppConstants.ordersEndpoint}/$orderId/status',
              'DioException $code: ${dioErr.response?.data ?? dioErr.message}',
              '{"Status": "$status"}',
            );
            await _db.deletePendingStatusUpdate(id);
          } else {
            _logger.w('🔌 Network error during status sync: ${dioErr.message}');
            break;
          }
        } catch (err) {
          _logger.e('❌ Unexpected error syncing status update id=$id', error: err);
          break;
        }
      }
    } catch (e) {
      _logger.e('❌ Error in syncPendingStatusUpdates', error: e);
    } finally {
      _isSyncingStatus = false;
    }
  }

  /// Clears all buffered GPS points.
  void clearBuffer() {
    _gpsPoints.clear();
    _lastBufferedLat = null;
    _lastBufferedLng = null;
    _lastBufferedHeading = null;
    _logger.i('🧹 GPS buffer cleared');
  }
}
