import 'package:logger/logger.dart';
import 'package:sqflite/sqflite.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

class LocalDatabaseLogStore {
  static Future<void> saveLocalErrorLog(
    Database db, {
    required String endpoint,
    required String errorMessage,
    required String payload,
  }) async {
    try {
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

  static Future<List<Map<String, dynamic>>> getLocalErrorLogs(Database db) async {
    try {
      return await db.query(
        'local_error_logs',
        orderBy: 'timestamp DESC',
      );
    } catch (e) {
      _logger.e('Failed to get local error logs', error: e);
      return [];
    }
  }

  static Future<void> clearLocalErrorLogs(Database db) async {
    try {
      await db.delete('local_error_logs');
      _logger.i('Cleared local error logs');
    } catch (e) {
      _logger.e('Failed to clear local error logs', error: e);
    }
  }
}
