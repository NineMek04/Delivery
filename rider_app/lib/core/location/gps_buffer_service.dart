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
import '../auth/auth_service.dart';
import '../auth/auth_constants.dart';

final gpsBufferServiceProvider = Provider<GpsBufferService>((ref) {
  return GpsBufferService(
    ref: ref,
    dio: ref.watch(deliveryApiClientProvider),
    db: ref.watch(localDatabaseServiceProvider),
  );
});

/// GPS Buffer & Batch Ingestion Service
///
/// Data Flow:
/// LocationService → SQLite queue → POST /api/v1/telemetry/gps/batch
class GpsBufferService {
  final Ref _ref;
  final Dio _dio;
  final LocalDatabaseService _db;
  final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

  Timer? _syncTimer;
  int _syncIntervalSeconds = 5;
  bool _isSyncing = false;
  bool _isSyncingStatus = false;

  // Adaptive sampling state
  double? _lastBufferedLat;
  double? _lastBufferedLng;
  double? _lastBufferedHeading;

  GpsBufferService({
    required Ref ref,
    required Dio dio,
    required LocalDatabaseService db,
  })  : _ref = ref,
        _dio = dio,
        _db = db;

  /// Starts the background periodic sync scheduler.
  void startSyncTimer() {
    final role = _ref.read(authServiceProvider.notifier).userRole;
    if (role != AuthConstants.roleRider) {
      _logger.d('⏳ Skipping GPS sync timer start: user is not a rider (role: $role)');
      return;
    }
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
    final role = _ref.read(authServiceProvider.notifier).userRole;
    if (role != AuthConstants.roleRider) {
      return;
    }
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

      await _db.savePendingGpsPoint(
        latitude: latitude,
        longitude: longitude,
        accuracy: accuracy,
        timestamp: DateTime.now().toUtc().toIso8601String(),
      );

      _logger.d('📍 Buffered GPS (${kIsWeb ? "web" : "mobile"}): ($latitude, $longitude)');

      // Proactive sync when buffer builds up
      if (await _db.getPendingGpsPointCount() >= 10 && !_isSyncing) {
        syncBufferedPoints();
      }
    } catch (e) {
      _logger.e('❌ Failed to buffer GPS point', error: e);
    }
  }

  /// Syncs buffered GPS points to the backend in batches of 100.
  Future<void> syncBufferedPoints() async {
    if (_isSyncing) return;

    final authStatus = _ref.read(authServiceProvider);
    if (authStatus != AuthStatus.authenticated) {
      _logger.d('⏳ Skipping GPS sync: not authenticated');
      return;
    }

    final role = _ref.read(authServiceProvider.notifier).userRole;
    if (role != AuthConstants.roleRider) {
      _logger.d('⏳ Skipping GPS sync: user is not a rider (role: $role)');
      return;
    }

    _isSyncing = true;

    try {
      final batch = await _db.getPendingGpsPoints(limit: 100);
      if (batch.isEmpty) return;
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
        await _db.deletePendingGpsPoints(batchIds);

        // Chain next batch if more remain, respecting the rate limit interval
        if (await _db.getPendingGpsPointCount() > 0) {
          final delaySeconds = _syncIntervalSeconds < 3 ? 3 : _syncIntervalSeconds;
          final delay = Duration(seconds: delaySeconds) + Duration(milliseconds: Random().nextInt(1000));
          Future.delayed(delay, syncBufferedPoints);
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
            final reconciled = response.statusCode != 401 &&
                await _isStatusAppliedOrSuperseded(orderId, status);
            if (reconciled) {
              await _db.deletePendingStatusUpdate(id);
              continue;
            }
            _logger.e('❌ 4xx error for orderId=$orderId — preserving update');
            await _db.saveLocalErrorLog(
              'PATCH ${AppConstants.ordersEndpoint}/$orderId/status',
              'Client Error ${response.statusCode}: ${response.statusMessage}',
              '{"Status": "$status"}',
            );
            break;
          } else {
            _logger.w('⚠️ Status update failed (${response.statusCode}) — will retry');
            break;
          }
        } on DioException catch (dioErr) {
          final code = dioErr.response?.statusCode;
          if (code != null && code >= 400 && code < 500) {
            final reconciled = code != 401 &&
                await _isStatusAppliedOrSuperseded(orderId, status);
            if (reconciled) {
              await _db.deletePendingStatusUpdate(id);
              continue;
            }
            _logger.e('❌ DioException 4xx for orderId=$orderId — preserving update');
            await _db.saveLocalErrorLog(
              'PATCH ${AppConstants.ordersEndpoint}/$orderId/status',
              'DioException $code: ${dioErr.response?.data ?? dioErr.message}',
              '{"Status": "$status"}',
            );
            break;
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
  Future<void> clearBuffer() async {
    await _db.clearPendingGpsPoints();
    _lastBufferedLat = null;
    _lastBufferedLng = null;
    _lastBufferedHeading = null;
    _logger.i('🧹 GPS buffer cleared');
  }

  Future<bool> _isStatusAppliedOrSuperseded(
    String orderId,
    String pendingStatus,
  ) async {
    try {
      final response = await _dio.get(
        '${AppConstants.ordersEndpoint}/$orderId',
      );
      final body = response.data;
      final value = body is Map<String, dynamic>
          ? (body['value'] ?? body['Value'] ?? body)
          : null;
      if (value is! Map) return false;

      final serverStatus = (value['status'] ?? value['Status'])
          ?.toString()
          .toUpperCase();
      final target = pendingStatus.toUpperCase();
      if (serverStatus == null) return false;
      if (serverStatus == target) return true;
      if (serverStatus == 'CANCELLED' || target == 'CANCELLED') return false;

      const order = {
        'CREATED': 0,
        'MATCHING': 1,
        'OFFERING': 2,
        'ASSIGNED': 3,
        'PICKING_UP': 4,
        'DELIVERING': 5,
        'COMPLETED': 6,
      };
      final serverRank = order[serverStatus];
      final targetRank = order[target];
      return serverRank != null &&
          targetRank != null &&
          serverRank > targetRank;
    } on DioException catch (error) {
      _logger.w(
        'Unable to reconcile pending status for orderId=$orderId: '
        '${error.message}',
      );
      return false;
    }
  }
}
