import 'dart:convert';
import 'package:flutter/foundation.dart';
import 'package:path/path.dart';
import 'package:sqflite/sqflite.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:logger/logger.dart';

import '../../models/order.dart';
import 'local_database_schema.dart';
import 'local_database_web_store.dart';
import 'local_database_gps_store.dart';
import 'local_database_log_store.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

class LocalDatabaseService {
  Database? _db;
  final LocalDatabaseWebStore _webStore = LocalDatabaseWebStore();

  Future<Database> get database async {
    if (_db != null) return _db!;
    _db = await _initDb();
    return _db!;
  }

  Future<Database> _initDb() async {
    final dbPath = await getDatabasesPath();
    final pathString = join(dbPath, LocalDatabaseSchema.databaseName);
    
    _logger.i('Opening SQLite database at: $pathString');

    return await openDatabase(
      pathString,
      version: LocalDatabaseSchema.databaseVersion,
      onUpgrade: LocalDatabaseSchema.onUpgrade,
      onCreate: LocalDatabaseSchema.onCreate,
    );
  }

  Future<void> saveOrders(List<OrderDto> orders) async {
    if (kIsWeb) {
      _webStore.saveOrders(orders, _isActiveStatus);
      return;
    }

    try {
      final db = await database;
      final batch = db.batch();
      
      for (final order in orders) {
        final isActive = _isActiveStatus(order.status) ? 1 : 0;
        final jsonData = jsonEncode(order.toJson());
        
        batch.insert(
          'orders',
          {
            'id': order.id,
            'status': order.status,
            'is_active': isActive,
            'json_data': jsonData,
          },
          conflictAlgorithm: ConflictAlgorithm.replace,
        );
      }
      
      await batch.commit(noResult: true);
      _logger.d('Saved ${orders.length} orders to local database');
    } catch (e) {
      _logger.e('Failed to save orders to local database', error: e);
    }
  }

  Future<void> saveOrder(OrderDto order) async {
    if (kIsWeb) {
      _webStore.saveOrder(order, _isActiveStatus);
      return;
    }

    try {
      final db = await database;
      final isActive = _isActiveStatus(order.status) ? 1 : 0;
      final jsonData = jsonEncode(order.toJson());

      await db.insert(
        'orders',
        {
          'id': order.id,
          'status': order.status,
          'is_active': isActive,
          'json_data': jsonData,
        },
        conflictAlgorithm: ConflictAlgorithm.replace,
      );
      _logger.d('Saved order ${order.id} to local database');
    } catch (e) {
      _logger.e('Failed to save order to local database', error: e);
    }
  }

  Future<List<OrderDto>> getActiveOrders() async {
    if (kIsWeb) {
      return _webStore.getActiveOrders();
    }

    try {
      final db = await database;
      final List<Map<String, dynamic>> maps = await db.query(
        'orders',
        where: 'is_active = ?',
        whereArgs: [1],
      );

      return maps.map((m) {
        final jsonMap = jsonDecode(m['json_data'] as String) as Map<String, dynamic>;
        return OrderDto.fromJson(jsonMap);
      }).toList();
    } catch (e) {
      _logger.e('Failed to get active orders from local database', error: e);
      return [];
    }
  }

  Future<List<OrderDto>> getCompletedOrders() async {
    if (kIsWeb) {
      return _webStore.getCompletedOrders();
    }

    try {
      final db = await database;
      final List<Map<String, dynamic>> maps = await db.query(
        'orders',
        where: 'is_active = ?',
        whereArgs: [0],
      );

      return maps.map((m) {
        final jsonMap = jsonDecode(m['json_data'] as String) as Map<String, dynamic>;
        return OrderDto.fromJson(jsonMap);
      }).toList();
    } catch (e) {
      _logger.e('Failed to get completed orders from local database', error: e);
      return [];
    }
  }

  Future<void> clearAllData() async {
    if (kIsWeb) {
      _webStore.clearAllData();
      return;
    }

    try {
      final db = await database;
      for (final table in ['orders', 'session', 'pending_status_updates', 'pending_gps_points', 'local_error_logs']) {
        await db.delete(table);
      }
      _logger.i('Cleared all local database tables');
    } catch (e) {
      _logger.e('Failed to clear local database data', error: e);
    }
  }

  Future<void> saveIsOnline(bool isOnline) async {
    if (kIsWeb) {
      _webStore.saveIsOnline(isOnline);
      return;
    }

    try {
      final db = await database;
      await db.insert(
        'session',
        {'key': 'is_online', 'value': isOnline.toString()},
        conflictAlgorithm: ConflictAlgorithm.replace,
      );
      _logger.d('Saved session status: is_online = $isOnline');
    } catch (e) {
      _logger.e('Failed to save online status to local database', error: e);
    }
  }

  Future<bool> getIsOnline() async {
    if (kIsWeb) return _webStore.getIsOnline();

    try {
      final db = await database;
      final maps = await db.query('session', where: 'key = ?', whereArgs: ['is_online']);
      return maps.isNotEmpty && (maps.first['value'] == 'true');
    } catch (e) {
      _logger.e('Failed to get online status from local database', error: e);
      return false;
    }
  }

  Future<void> savePendingStatusUpdate(String orderId, String status) async {
    if (kIsWeb) {
      _webStore.savePendingStatusUpdate(orderId, status);
      return;
    }

    try {
      final db = await database;
      await db.insert(
        'pending_status_updates',
        {
          'order_id': orderId,
          'status': status,
          'timestamp': DateTime.now().millisecondsSinceEpoch,
        },
      );
      _logger.i('Saved pending status update locally: orderId=$orderId, status=$status');
    } catch (e) {
      _logger.e('Failed to save pending status update', error: e);
      rethrow;
    }
  }

  Future<List<Map<String, dynamic>>> getPendingStatusUpdates() async {
    if (kIsWeb) {
      return _webStore.getPendingStatusUpdates();
    }

    try {
      final db = await database;
      return await db.query(
        'pending_status_updates',
        orderBy: 'timestamp ASC',
      );
    } catch (e) {
      _logger.e('Failed to get pending status updates', error: e);
      return [];
    }
  }

  Future<void> deletePendingStatusUpdate(int id) async {
    if (kIsWeb) {
      _webStore.deletePendingStatusUpdate(id);
      return;
    }

    try {
      final db = await database;
      await db.delete(
        'pending_status_updates',
        where: 'id = ?',
        whereArgs: [id],
      );
      _logger.i('Deleted pending status update: id=$id');
    } catch (e) {
      _logger.e('Failed to delete pending status update', error: e);
    }
  }

  Future<void> savePendingGpsPoint({
    required double latitude,
    required double longitude,
    required double accuracy,
    required String timestamp,
  }) async {
    final createdAt = DateTime.now().millisecondsSinceEpoch;
    if (kIsWeb) {
      _webStore.savePendingGpsPoint(
        latitude: latitude,
        longitude: longitude,
        accuracy: accuracy,
        timestamp: timestamp,
        createdAt: createdAt,
      );
      return;
    }

    final db = await database;
    await LocalDatabaseGpsStore.savePendingGpsPoint(
      db,
      latitude: latitude,
      longitude: longitude,
      accuracy: accuracy,
      timestamp: timestamp,
      createdAt: createdAt,
    );
  }

  Future<List<Map<String, dynamic>>> getPendingGpsPoints({
    int limit = 100,
  }) async {
    if (kIsWeb) {
      return _webStore.getPendingGpsPoints(limit);
    }

    final db = await database;
    return LocalDatabaseGpsStore.getPendingGpsPoints(db, limit: limit);
  }

  Future<int> getPendingGpsPointCount() async {
    if (kIsWeb) return _webStore.getPendingGpsPointCount();
    final db = await database;
    return LocalDatabaseGpsStore.getPendingGpsPointCount(db);
  }

  Future<void> deletePendingGpsPoints(Iterable<int> ids) async {
    if (kIsWeb) {
      _webStore.deletePendingGpsPoints(ids);
      return;
    }

    final db = await database;
    await LocalDatabaseGpsStore.deletePendingGpsPoints(db, ids);
  }

  Future<void> clearPendingGpsPoints() async {
    if (kIsWeb) {
      _webStore.clearPendingGpsPoints();
      return;
    }
    final db = await database;
    await LocalDatabaseGpsStore.clearPendingGpsPoints(db);
  }

  Future<void> saveLocalErrorLog(String endpoint, String errorMessage, String payload) async {
    if (kIsWeb) {
      _webStore.saveLocalErrorLog(endpoint, errorMessage, payload);
      return;
    }

    final db = await database;
    await LocalDatabaseLogStore.saveLocalErrorLog(
      db,
      endpoint: endpoint,
      errorMessage: errorMessage,
      payload: payload,
    );
  }

  Future<List<Map<String, dynamic>>> getLocalErrorLogs() async {
    if (kIsWeb) {
      return _webStore.getLocalErrorLogs();
    }

    final db = await database;
    return LocalDatabaseLogStore.getLocalErrorLogs(db);
  }

  Future<void> clearLocalErrorLogs() async {
    if (kIsWeb) {
      _webStore.clearLocalErrorLogs();
      return;
    }

    final db = await database;
    await LocalDatabaseLogStore.clearLocalErrorLogs(db);
  }

  bool _isActiveStatus(String status) {
    final s = status.toUpperCase();
    return s == 'OFFERING' || s == 'ASSIGNED' || s == 'PICKING_UP' || s == 'DELIVERING' || s == 'MATCHING';
  }
}

final localDatabaseServiceProvider = Provider<LocalDatabaseService>((ref) {
  return LocalDatabaseService();
});
