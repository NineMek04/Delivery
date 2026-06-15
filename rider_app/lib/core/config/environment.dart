import '../api/web_url_resolver_stub.dart'
    if (dart.library.html) '../api/web_url_resolver_web.dart';

/// Environment configuration for the Rider App.
///
/// ─── Build targets ───────────────────────────────────────────────────────
/// Docker Web (production/test):
///   API_BASE_URL is left EMPTY → nginx same-origin proxy handles /api/ & /hubs/
///
/// Android Emulator (local dev):
///   flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5000
///
/// Physical device / LAN (local dev):
///   flutter run --dart-define=API_BASE_URL=http://192.168.x.x:5000
/// ─────────────────────────────────────────────────────────────────────────
class Environment {
  Environment._();

  /// Empty string → use same-origin nginx proxy (correct for Docker Web).
  /// Override with --dart-define=API_BASE_URL=<url> for native device dev.
  static const String apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: '', // ← empty = same-origin (Docker/Web). NOT 10.0.2.2
  );

  static const String apiPrefix = '/api/v1';

  static String get apiUrl {
    if (apiBaseUrl.isEmpty) {
      final origin = getWindowOrigin();
      if (origin.isNotEmpty) {
        return '$origin$apiPrefix';
      }
      return apiPrefix;
    }
    final base = apiBaseUrl.endsWith('/')
        ? apiBaseUrl.substring(0, apiBaseUrl.length - 1)
        : apiBaseUrl;
    return '$base$apiPrefix';
  }

  static String get signalRUrl {
    if (apiBaseUrl.isEmpty) {
      final origin = getWindowOrigin();
      if (origin.isNotEmpty) {
        return '$origin/hubs/tracking';
      }
      return '/hubs/tracking';
    }
    final base = apiBaseUrl.endsWith('/')
        ? apiBaseUrl.substring(0, apiBaseUrl.length - 1)
        : apiBaseUrl;
    return '$base/hubs/tracking';
  }

  static String get chatHubUrl {
    if (apiBaseUrl.isEmpty) {
      final origin = getWindowOrigin();
      if (origin.isNotEmpty) {
        return '$origin/hubs/chat';
      }
      return '/hubs/chat';
    }
    final base = apiBaseUrl.endsWith('/')
        ? apiBaseUrl.substring(0, apiBaseUrl.length - 1)
        : apiBaseUrl;
    return '$base/hubs/chat';
  }

  static const bool isDevelopment = bool.fromEnvironment(
    'DEBUG',
    defaultValue: false,
  );

  static const bool enableHttpLogging = bool.fromEnvironment(
    'HTTP_LOGGING',
    defaultValue: false,
  );

  /// Demo-only fallback. Production builds must never publish a fabricated
  /// rider position when browser geolocation is unavailable.
  static const bool enableMockGps = bool.fromEnvironment(
    'ENABLE_MOCK_GPS',
    defaultValue: true,
  );

  static const Duration connectTimeout = Duration(seconds: 15);
  static const Duration receiveTimeout = Duration(seconds: 15);
  static const int gpsDistanceFilter = 10;
  static const int gpsUpdateIntervalSeconds = 5;
  static const int offerCountdownSeconds = 30;
}
