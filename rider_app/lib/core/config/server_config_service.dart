import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../auth/safe_storage.dart';
import 'environment.dart';

/// Result from testing server connectivity.
class ServerConnectionResult {
  final bool success;
  final int? statusCode;
  final int? latencyMs;
  final String message;

  const ServerConnectionResult({
    required this.success,
    this.statusCode,
    this.latencyMs,
    required this.message,
  });
}

/// Service to persist, retrieve, and test custom Server Base URLs (e.g. Cloudflare Tunnels).
class ServerConfigService {
  static const String storageKey = 'custom_server_base_url';
  final SafeStorage _storage;

  ServerConfigService({SafeStorage? storage}) : _storage = storage ?? SafeStorage();

  /// Retrieve the saved custom server URL from local storage.
  Future<String?> getSavedServerUrl() async {
    try {
      final saved = await _storage.read(key: storageKey);
      if (saved != null && saved.trim().isNotEmpty) {
        return _normalizeUrl(saved);
      }
    } catch (e) {
      debugPrint('[ServerConfigService] Error reading saved URL: $e');
    }
    return null;
  }

  /// Save custom server URL to local storage and update Environment.
  Future<void> saveServerUrl(String url) async {
    final normalized = _normalizeUrl(url);
    await _storage.write(key: storageKey, value: normalized);
    Environment.setCustomBaseUrl(normalized);
    debugPrint('[ServerConfigService] Saved server URL: $normalized');
  }

  /// Clear saved server URL and restore default.
  Future<void> clearServerUrl() async {
    await _storage.delete(key: storageKey);
    Environment.setCustomBaseUrl(null);
    debugPrint('[ServerConfigService] Cleared custom server URL, restored default: ${Environment.apiBaseUrl}');
  }

  /// Test connectivity to a server URL by requesting the `/health` endpoint.
  Future<ServerConnectionResult> testConnection(String url) async {
    final normalized = _normalizeUrl(url);
    if (!normalized.startsWith('http://') && !normalized.startsWith('https://')) {
      return const ServerConnectionResult(
        success: false,
        message: 'URL ต้องขึ้นต้นด้วย http:// หรือ https://',
      );
    }

    final stopwatch = Stopwatch()..start();
    try {
      final dio = Dio(
        BaseOptions(
          connectTimeout: const Duration(seconds: 6),
          receiveTimeout: const Duration(seconds: 6),
          headers: {
            'Accept': '*/*',
          },
        ),
      );

      final response = await dio.get('$normalized/health');
      stopwatch.stop();

      if (response.statusCode == 200) {
        return ServerConnectionResult(
          success: true,
          statusCode: 200,
          latencyMs: stopwatch.elapsedMilliseconds,
          message: 'เชื่อมต่อสำเร็จ (${stopwatch.elapsedMilliseconds} ms)',
        );
      } else {
        return ServerConnectionResult(
          success: false,
          statusCode: response.statusCode,
          latencyMs: stopwatch.elapsedMilliseconds,
          message: 'เซิร์ฟเวอร์ตอบกลับสถานะ ${response.statusCode}',
        );
      }
    } on DioException catch (e) {
      stopwatch.stop();
      String errorMsg = 'ไม่สามารถเชื่อมต่อกับเซิร์ฟเวอร์ได้';
      if (e.type == DioExceptionType.connectionTimeout ||
          e.type == DioExceptionType.sendTimeout ||
          e.type == DioExceptionType.receiveTimeout) {
        errorMsg = 'การเชื่อมต่อหมดเวลา (Timeout) กรุณาตรวจสอบสถานะเซิร์ฟเวอร์';
      } else if (e.response != null) {
        errorMsg = 'เซิร์ฟเวอร์ตอบกลับรหัส: ${e.response?.statusCode}';
      } else if (e.error != null) {
        errorMsg = 'ข้อผิดพลาด: ${e.error}';
      }
      return ServerConnectionResult(
        success: false,
        latencyMs: stopwatch.elapsedMilliseconds,
        message: errorMsg,
      );
    } catch (e) {
      stopwatch.stop();
      return ServerConnectionResult(
        success: false,
        latencyMs: stopwatch.elapsedMilliseconds,
        message: 'เกิดข้อผิดพลาด: $e',
      );
    }
  }

  /// Strip trailing slashes and whitespace.
  String _normalizeUrl(String url) {
    var trimmed = url.trim();
    while (trimmed.endsWith('/')) {
      trimmed = trimmed.substring(0, trimmed.length - 1);
    }
    return trimmed;
  }
}

/// Provider for ServerConfigService.
final serverConfigServiceProvider = Provider<ServerConfigService>((ref) {
  return ServerConfigService();
});

/// Reactive StateProvider for the active server base URL.
final serverUrlProvider = StateProvider<String>((ref) {
  return Environment.apiBaseUrl;
});
