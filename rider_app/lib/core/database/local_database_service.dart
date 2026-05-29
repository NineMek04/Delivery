import 'dart:convert';
import 'package:path/path.dart';
import 'package:sqflite/sqflite.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:logger/logger.dart';

import '../../models/order.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

class LocalDatabaseService {
  Database? _db;

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
      version: 1,
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
      },
    );
  }

  // Save multiple orders (replaces the ones with the same ID)
  Future<void> saveOrders(List<OrderDto> orders) async {
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
    try {
      final db = await database;
      await db.delete('orders');
      await db.delete('session');
      _logger.i('Cleared all local database tables');
    } catch (e) {
      _logger.e('Failed to clear local database data', error: e);
    }
  }

  // Save online status
  Future<void> saveIsOnline(bool isOnline) async {
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

  bool _isActiveStatus(String status) {
    final s = status.toUpperCase();
    return s == 'OFFERING' || s == 'ASSIGNED' || s == 'PICKING_UP' || s == 'DELIVERING' || s == 'MATCHING';
  }
}

final localDatabaseServiceProvider = Provider<LocalDatabaseService>((ref) {
  return LocalDatabaseService();
});
