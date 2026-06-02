import 'dart:async';
import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:isar/isar.dart';
import 'package:logger/logger.dart';
import 'package:sqflite/sqflite.dart' show getDatabasesPath;
import '../api/delivery_api_client.dart';
import '../../models/gps_point.dart';
import '../database/local_database_service.dart';
import '../config/app_constants.dart';

final gpsBufferServiceProvider = Provider<GpsBufferService>((ref) {
  return GpsBufferService(
    dio: ref.watch(deliveryApiClientProvider),
    db: ref.watch(localDatabaseServiceProvider),
  );
});

/// High-Performance Offline Location Buffer & Batch Ingestion Service (Isar NoSQL Engine)
/// 
/// This service acts as an offline shield for GPS points collected when:
/// 1. The device is offline or has intermittent network connectivity.
/// 2. The backend is throttled due to high load (rate-limited / backpressure).
/// 
/// Data Flow:
/// Geolocator stream -> LocationService -> GpsBufferService -> Isar (gpsPoints collection)
/// Isar (gpsPoints collection) -> Batch uploads (POST /api/telemetry/gps/batch) -> Purge on 200 OK
class GpsBufferService {
  final Dio _dio;
  final LocalDatabaseService _db;
  final _logger = Logger(printer: PrettyPrinter(methodCount: 0));
  
  Isar? _isar;
  Timer? _syncTimer;
  int _syncIntervalSeconds = 5; // Default sync check interval
  bool _isSyncing = false;
  bool _isSyncingStatus = false;

  GpsBufferService({
    required Dio dio,
    required LocalDatabaseService db,
  })  : _dio = dio,
        _db = db;

  /// Retrieves or opens the Isar database instance.
  /// Reuses SQLite directory path to keep all local files organized together.
  Future<Isar> _getIsar() async {
    if (_isar != null) return _isar!;
    
    final dbDir = await getDatabasesPath();
    _logger.i('📂 Opening Isar database in path: $dbDir');
    
    _isar = await Isar.open(
      [GpsPointSchema],
      directory: dbDir,
    );
    return _isar!;
  }

  /// Starts the background periodic synchronize scheduler.
  Future<void> startSyncTimer() async {
    _syncTimer?.cancel();
    _logger.i('🛰️ Starting periodic GPS Isar Offline Sync Timer (every $_syncIntervalSeconds seconds)');
    _syncTimer = Timer.periodic(Duration(seconds: _syncIntervalSeconds), (_) {
      syncBufferedPoints();
      syncPendingStatusUpdates();
    });
  }

  /// Stops the periodic synchronizer.
  void stopSyncTimer() {
    _syncTimer?.cancel();
    _syncTimer = null;
    _logger.i('🛑 Stopped GPS Isar Offline Sync Timer');
  }

  /// Adjusts sync frequency dynamically based on backpressure warnings.
  void updateSyncInterval(int intervalSeconds) {
    if (intervalSeconds < 3) intervalSeconds = 3; // Enterprise guard: never spam under 3s
    if (_syncIntervalSeconds != intervalSeconds) {
      _logger.i('🔄 Dynamically adjusting telemetry sync interval: $_syncIntervalSeconds -> $intervalSeconds seconds');
      _syncIntervalSeconds = intervalSeconds;
      if (_syncTimer != null) {
        startSyncTimer();
      }
    }
  }

  /// Buffers a single location coordinate offline using Isar NoSQL.
  Future<void> bufferLocation(double latitude, double longitude, double accuracy) async {
    try {
      final isar = await _getIsar();
      
      final point = GpsPoint()
        ..latitude = latitude
        ..longitude = longitude
        ..accuracy = accuracy
        ..timestamp = DateTime.now().toUtc().toIso8601String();

      // Write to Isar via asynchronous transaction block
      await isar.writeTxn(() async {
        await isar.gpsPoints.put(point);
      });
      
      _logger.d('📍 Buffered GPS point in Isar locally: ($latitude, $longitude)');

      // Proactive sync trigger: If queue is building up, trigger sync immediately
      final count = await isar.gpsPoints.count();
      if (count >= 10 && !_isSyncing) {
        syncBufferedPoints();
      }
    } catch (e) {
      _logger.e('❌ Failed to buffer location in Isar', error: e);
    }
  }

  /// Chunks buffered GPS points from Isar database and syncs them to the backend REST endpoint.
  Future<void> syncBufferedPoints() async {
    if (_isSyncing) return;
    _isSyncing = true;

    try {
      final isar = await _getIsar();
      
      // Query up to 100 points ordered by chronological ID ascending (FIFO)
      final points = await isar.gpsPoints
          .where()
          .limit(100)
          .findAll();
      
      if (points.isEmpty) {
        _isSyncing = false;
        return;
      }

      _logger.d('📡 Syncing batch of ${points.length} buffered GPS points from Isar...');

      // Transform Isar records into JSON array matching C# GpsBatchPointRequest
      final List<Map<String, dynamic>> payload = points.map((p) {
        return {
          'Latitude': p.latitude,
          'Longitude': p.longitude,
          'Accuracy': p.accuracy,
          'Timestamp': p.timestamp,
        };
      }).toList();

      final List<Id> pointIds = points.map((p) => p.id).toList();

      final response = await _dio.post(
        'telemetry/gps/batch',
        data: payload,
      );

      // Check header recommendation to adjust synchronization interval
      final pingHeader = response.headers.value('X-Recommended-Ping');
      if (pingHeader != null) {
        final newInterval = int.tryParse(pingHeader);
        if (newInterval != null) {
          updateSyncInterval(newInterval);
        }
      }

      if (response.statusCode == 200) {
        // Success: Clean successfully saved points out of Isar DB
        _logger.i('✅ Batch upload of ${points.length} GPS points succeeded. Purging Isar buffer.');
        
        await isar.writeTxn(() async {
          await isar.gpsPoints.deleteAll(pointIds);
        });
        
        // If there are still backlogged items, run chained synchronization
        final remainingCount = await isar.gpsPoints.count();
        if (remainingCount > 0) {
          Future.delayed(const Duration(milliseconds: 100), () => syncBufferedPoints());
        }
      } else if (response.statusCode == 429) {
        // 429 Too Many Requests: Rate-limited/throttled. KEEP coordinates locally and backoff
        _logger.w('⚠️ Ingestion batch throttled (429 Too Many Requests). Keeping points in Isar and slowing sync.');
      } else {
        _logger.w('⚠️ Ingestion batch returned status: ${response.statusCode}. Keeping points in Isar.');
      }
    } on DioException catch (e) {
      if (e.response?.statusCode == 429) {
        _logger.w('⚠️ Ingestion batch throttled via exception (429 Too Many Requests). Keeping points in Isar and slowing sync.');
      } else {
        _logger.w('🔌 Network connectivity issue while uploading GPS batch: ${e.message}. Keeping points.');
      }
      
      // Parse header recommendations from error responses too
      if (e.response != null) {
        final statusCode = e.response!.statusCode;
        final pingHeader = e.response!.headers.value('X-Recommended-Ping');
        if (pingHeader != null) {
          final newInterval = int.tryParse(pingHeader);
          if (newInterval != null) {
            updateSyncInterval(newInterval);
          }
        }
      }
    } catch (e) {
      _logger.e('❌ Unexpected error during offline Isar telemetry synchronization', error: e);
    } finally {
      _isSyncing = false;
    }
  }

  /// Synchronizes pending order status updates queued during offline mode.
  Future<void> syncPendingStatusUpdates() async {
    if (_isSyncingStatus) return;
    _isSyncingStatus = true;

    try {
      final pendingList = await _db.getPendingStatusUpdates();
      if (pendingList.isEmpty) {
        _isSyncingStatus = false;
        return;
      }

      _logger.d('📡 Syncing ${pendingList.length} pending order status updates from SQLite...');

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
            _logger.i('✅ Successfully synced pending order status update: orderId=$orderId, status=$status');
            await _db.deletePendingStatusUpdate(id);
          } else {
            _logger.w('⚠️ Failed to sync status update, status code: ${response.statusCode}');
            break;
          }
        } on DioException catch (dioErr) {
          _logger.w('🔌 Network error during status sync: ${dioErr.message}');
          break;
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

  /// Clears all buffered GPS points from Isar database to prevent data contamination across user sessions.
  Future<void> clearBuffer() async {
    try {
      final isar = await _getIsar();
      await isar.writeTxn(() async {
        await isar.gpsPoints.clear();
      });
      _logger.i('🧹 Successfully cleared Isar offline GPS buffer.');
    } catch (e) {
      _logger.e('❌ Failed to clear GPS buffer in Isar', error: e);
    }
  }
}
