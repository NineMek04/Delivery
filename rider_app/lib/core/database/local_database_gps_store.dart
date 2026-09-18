import 'package:sqflite/sqflite.dart';

class LocalDatabaseGpsStore {
  static Future<void> savePendingGpsPoint(
    Database db, {
    required double latitude,
    required double longitude,
    required double accuracy,
    required String timestamp,
    required int createdAt,
  }) async {
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

  static Future<List<Map<String, dynamic>>> getPendingGpsPoints(
    Database db, {
    int limit = 100,
  }) async {
    return db.query(
      'pending_gps_points',
      orderBy: 'created_at ASC, id ASC',
      limit: limit,
    );
  }

  static Future<int> getPendingGpsPointCount(Database db) async {
    return Sqflite.firstIntValue(
          await db.rawQuery('SELECT COUNT(*) FROM pending_gps_points'),
        ) ??
        0;
  }

  static Future<void> deletePendingGpsPoints(Database db, Iterable<int> ids) async {
    final idList = ids.toList(growable: false);
    if (idList.isEmpty) return;
    final placeholders = List.filled(idList.length, '?').join(',');
    await db.delete(
      'pending_gps_points',
      where: 'id IN ($placeholders)',
      whereArgs: idList,
    );
  }

  static Future<void> clearPendingGpsPoints(Database db) async {
    await db.delete('pending_gps_points');
  }
}
