import 'package:logger/logger.dart';
import 'package:sqflite/sqflite.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

class LocalDatabaseSchema {
  static const String databaseName = 'delivery_rider.db';
  static const int databaseVersion = 4;

  static Future<void> onCreate(Database db, int version) async {
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

    _logger.i('Created database tables (v$databaseVersion)');
  }

  static Future<void> onUpgrade(Database db, int oldVersion, int newVersion) async {
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
  }
}
