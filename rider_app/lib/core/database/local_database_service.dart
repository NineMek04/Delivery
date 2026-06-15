import 'dart:convert';
import 'package:flutter/foundation.dart';
import 'package:path/path.dart';
import 'package:sqflite/sqflite.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:logger/logger.dart';

import '../../models/order.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

class LocalDatabaseService {
  Database? _db;

  // Web fallback storage
  final Map<String, Map<String, dynamic>> _webOrders = {};
  final Map<String, String> _webSession = {};
  final List<Map<String, dynamic>> _webPendingUpdates = [];
  final List<Map<String, dynamic>> _webPendingGpsPoints = [];
  final List<Map<String, dynamic>> _webErrorLogs = [];
  int _webGpsSequence = 0;

  Future<Database> get database async {
    if (_db != null) return _db!;
    _db = await _initDb();
    return _db!;
  }

  Future<Database> _initDb() async {
    final dbPath = await getDatabasesPath();
    final pathString = join(dbPath, 'delivery_rider.db');
    
    _logger.i('Opening SQLite database at: $pathString');

    return await openDatabase(
      pathString,
      version: 4,
      onUpgrade: (db, oldVersion, newVersion) async {
        if (oldVersion < 2) {
          await db.execute('''
            CREATE TABLE pending_status_updates (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              order_id TEXT,
              status TEXT,
              timestamp INTEGER
            )
          ''');
          _logger.i('Upgraded database schema: created pending_status_updates table');
        }
        if (oldVersion < 3) {
          await db.execute('''
            CREATE TABLE local_error_logs (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              timestamp INTEGER,
              endpoint TEXT,
              error_message TEXT,
              payload TEXT
            )
          ''');
          _logger.i('Upgraded database schema: created local_error_logs table');
        }
        if (oldVersion < 4) {
          await db.execute('''
            CREATE TABLE pending_gps_points (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              latitude REAL NOT NULL,
              longitude REAL NOT NULL,
              accuracy REAL NOT NULL,
              timestamp TEXT NOT NULL,
              created_at INTEGER NOT NULL
            )
          ''');
          await db.execute(
            'CREATE INDEX IX_pending_gps_points_created_at '
            'ON pending_gps_points (created_at, id)',
          );
          _logger.i('Upgraded database schema: created pending_gps_points table');
        }
      },
      onCreate: (db, version) async {
        await db.execute('''
          CREATE TABLE orders (
            id TEXT PRIMARY KEY,
            status TEXT,
            is_active INTEGER,
            json_data TEXT
          )
        ''');

        await db.execute('''
          CREATE TABLE session (
            key TEXT PRIMARY KEY,
            value TEXT
          )
        ''');

        await db.execute('''
          CREATE TABLE pending_status_updates (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            order_id TEXT,
            status TEXT,
            timestamp INTEGER
          )
        ''');

        await db.execute('''
          CREATE TABLE local_error_logs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp INTEGER,
            endpoint TEXT,
            error_message TEXT,
            payload TEXT
          )
        ''');
        await db.execute('''
          CREATE TABLE pending_gps_points (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            latitude REAL NOT NULL,
            longitude REAL NOT NULL,
            accuracy REAL NOT NULL,
            timestamp TEXT NOT NULL,
            created_at INTEGER NOT NULL
          )
        ''');
        await db.execute(
          'CREATE INDEX IX_pending_gps_points_created_at '
          'ON pending_gps_points (created_at, id)',
        );
        _logger.i('Created database tables (v4)');
      },
    );
  }

  // Save multiple orders (replaces the ones with the same ID)
  Future<void> saveOrders(List<OrderDto> orders) async {
    if (kIsWeb) {
      for (final order in orders) {
        _webOrders[order.id] = {
          'id': order.id,
          'status': order.status,
          'is_active': _isActiveStatus(order.status) ? 1 : 0,
          'json_data': jsonEncode(order.toJson()),
        };
      }
      _logger.d('[Web] Saved ${orders.length} orders to memory');
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

  // Save/Update a single order
  Future<void> saveOrder(OrderDto order) async {
    if (kIsWeb) {
      _webOrders[order.id] = {
        'id': order.id,
        'status': order.status,
        'is_active': _isActiveStatus(order.status) ? 1 : 0,
        'json_data': jsonEncode(order.toJson()),
      };
      _logger.d('[Web] Saved order ${order.id} to memory');
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

  // Get all active orders
  Future<List<OrderDto>> getActiveOrders() async {
    if (kIsWeb) {
      final active = _webOrders.values
          .where((m) => m['is_active'] == 1)
          .map((m) => OrderDto.fromJson(jsonDecode(m['json_data'] as String) as Map<String, dynamic>))
          .toList();
      return active;
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

  // Get completed orders
  Future<List<OrderDto>> getCompletedOrders() async {
    if (kIsWeb) {
      final completed = _webOrders.values
          .where((m) => m['is_active'] == 0)
          .map((m) => OrderDto.fromJson(jsonDecode(m['json_data'] as String) as Map<String, dynamic>))
          .toList();
      return completed;
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

  // Clear all data (e.g. on logout)
  Future<void> clearAllData() async {
    if (kIsWeb) {
      _webOrders.clear();
      _webSession.clear();
      _webPendingUpdates.clear();
      _webPendingGpsPoints.clear();
      _webErrorLogs.clear();
      _logger.i('[Web] Cleared memory database');
      return;
    }

    try {
      final db = await database;
      await db.delete('orders');
      await db.delete('session');
      await db.delete('pending_status_updates');
      await db.delete('pending_gps_points');
      await db.delete('local_error_logs');
      _logger.i('Cleared all local database tables');
    } catch (e) {
      _logger.e('Failed to clear local database data', error: e);
    }
  }

  // Save online status
  Future<void> saveIsOnline(bool isOnline) async {
    if (kIsWeb) {
      _webSession['is_online'] = isOnline.toString();
      _logger.d('[Web] Saved session status: is_online = $isOnline');
      return;
    }

    try {
      final db = await database;
      await db.insert(
        'session',
        {
          'key': 'is_online',
          'value': isOnline.toString(),
        },
        conflictAlgorithm: ConflictAlgorithm.replace,
      );
      _logger.d('Saved session status: is_online = $isOnline');
    } catch (e) {
      _logger.e('Failed to save online status to local database', error: e);
    }
  }

  // Get online status
  Future<bool> getIsOnline() async {
    if (kIsWeb) {
      final val = _webSession['is_online'];
      return val == 'true';
    }

    try {
      final db = await database;
      final List<Map<String, dynamic>> maps = await db.query(
        'session',
        where: 'key = ?',
        whereArgs: ['is_online'],
      );

      if (maps.isEmpty) return false;
      final val = maps.first['value'] as String;
      return val == 'true';
    } catch (e) {
      _logger.e('Failed to get online status from local database', error: e);
      return false;
    }
  }



  // Save pending status update for offline sync
  Future<void> savePendingStatusUpdate(String orderId, String status) async {
    if (kIsWeb) {
      _webPendingUpdates.add({
        'id': _webPendingUpdates.length + 1,
        'order_id': orderId,
        'status': status,
        'timestamp': DateTime.now().millisecondsSinceEpoch,
      });
      _logger.d('[Web] Saved pending status update to memory: orderId=$orderId, status=$status');
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

  // Get all pending status updates (FIFO order)
  Future<List<Map<String, dynamic>>> getPendingStatusUpdates() async {
    if (kIsWeb) {
      return List<Map<String, dynamic>>.from(_webPendingUpdates);
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

  // Delete pending status update
  Future<void> deletePendingStatusUpdate(int id) async {
    if (kIsWeb) {
      _webPendingUpdates.removeWhere((item) => item['id'] == id);
      _logger.d('[Web] Deleted pending status update from memory: id=$id');
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
      _webPendingGpsPoints.add({
        'id': ++_webGpsSequence,
        'latitude': latitude,
        'longitude': longitude,
        'accuracy': accuracy,
        'timestamp': timestamp,
        'created_at': createdAt,
      });
      if (_webPendingGpsPoints.length > 10000) {
        _webPendingGpsPoints.removeRange(
          0,
          _webPendingGpsPoints.length - 10000,
        );
      }
      return;
    }

    final db = await database;
    await db.transaction((txn) async {
      await txn.insert('pending_gps_points', {
        'latitude': latitude,
        'longitude': longitude,
        'accuracy': accuracy,
        'timestamp': timestamp,
        'created_at': createdAt,
      });
      final count = Sqflite.firstIntValue(
            await txn.rawQuery('SELECT COUNT(*) FROM pending_gps_points'),
          ) ??
          0;
      final overflow = count - 10000;
      if (overflow > 0) {
        await txn.rawDelete('''
          DELETE FROM pending_gps_points
          WHERE id IN (
            SELECT id FROM pending_gps_points
            ORDER BY created_at ASC, id ASC
            LIMIT ?
          )
        ''', [overflow]);
      }
    });
  }

  Future<List<Map<String, dynamic>>> getPendingGpsPoints({
    int limit = 100,
  }) async {
    if (kIsWeb) {
      return List<Map<String, dynamic>>.from(
        _webPendingGpsPoints.take(limit),
      );
    }

    final db = await database;
    return db.query(
      'pending_gps_points',
      orderBy: 'created_at ASC, id ASC',
      limit: limit,
    );
  }

  Future<int> getPendingGpsPointCount() async {
    if (kIsWeb) return _webPendingGpsPoints.length;
    final db = await database;
    return Sqflite.firstIntValue(
          await db.rawQuery('SELECT COUNT(*) FROM pending_gps_points'),
        ) ??
        0;
  }

  Future<void> deletePendingGpsPoints(Iterable<int> ids) async {
    final idList = ids.toList(growable: false);
    if (idList.isEmpty) return;

    if (kIsWeb) {
      final idSet = idList.toSet();
      _webPendingGpsPoints.removeWhere((item) => idSet.contains(item['id']));
      return;
    }

    final db = await database;
    final placeholders = List.filled(idList.length, '?').join(',');
    await db.delete(
      'pending_gps_points',
      where: 'id IN ($placeholders)',
      whereArgs: idList,
    );
  }

  Future<void> clearPendingGpsPoints() async {
    if (kIsWeb) {
      _webPendingGpsPoints.clear();
      return;
    }
    final db = await database;
    await db.delete('pending_gps_points');
  }

  // Save local error log
  Future<void> saveLocalErrorLog(String endpoint, String errorMessage, String payload) async {
    if (kIsWeb) {
      _webErrorLogs.add({
        'id': _webErrorLogs.length + 1,
        'timestamp': DateTime.now().millisecondsSinceEpoch,
        'endpoint': endpoint,
        'error_message': errorMessage,
        'payload': payload,
      });
      _logger.d('[Web] Saved local error log to memory: endpoint=$endpoint');
      return;
    }

    try {
      final db = await database;
      await db.insert(
        'local_error_logs',
        {
          'timestamp': DateTime.now().millisecondsSinceEpoch,
          'endpoint': endpoint,
          'error_message': errorMessage,
          'payload': payload,
        },
      );
      _logger.w('Logged local API error: endpoint=$endpoint, error=$errorMessage');
    } catch (e) {
      _logger.e('Failed to save local error log', error: e);
    }
  }

  // Get all local error logs
  Future<List<Map<String, dynamic>>> getLocalErrorLogs() async {
    if (kIsWeb) {
      return List<Map<String, dynamic>>.from(_webErrorLogs);
    }

    try {
      final db = await database;
      return await db.query(
        'local_error_logs',
        orderBy: 'timestamp DESC',
      );
    } catch (e) {
      _logger.e('Failed to get local error logs', error: e);
      return [];
    }
  }

  // Clear local error logs
  Future<void> clearLocalErrorLogs() async {
    if (kIsWeb) {
      _webErrorLogs.clear();
      return;
    }

    try {
      final db = await database;
      await db.delete('local_error_logs');
      _logger.i('Cleared local error logs');
    } catch (e) {
      _logger.e('Failed to clear local error logs', error: e);
    }
  }

  bool _isActiveStatus(String status) {
    final s = status.toUpperCase();
    return s == 'OFFERING' || s == 'ASSIGNED' || s == 'PICKING_UP' || s == 'DELIVERING' || s == 'MATCHING';
  }
}

final localDatabaseServiceProvider = Provider<LocalDatabaseService>((ref) {
  return LocalDatabaseService();
});
