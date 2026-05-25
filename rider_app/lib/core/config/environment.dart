/// Environment configuration for the Rider App.
///
/// Docker Web (default): same-origin `/api/v1` + `/hubs/tracking` via nginx proxy.
/// Native dev: `--dart-define=API_BASE_URL=http://10.0.2.2:5000`
class Environment {
  Environment._();

  /// Empty = same-origin (Docker nginx หรือ reverse proxy).
  static const String apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://10.0.2.2:5000', // Changed to Emulator default
  );

  static const String apiPrefix = '/api/v1';

  static String get apiUrl {
    if (apiBaseUrl.isEmpty) return apiPrefix;
    final base = apiBaseUrl.endsWith('/')
        ? apiBaseUrl.substring(0, apiBaseUrl.length - 1)
        : apiBaseUrl;
    return '$base$apiPrefix';
  }

  static String get signalRUrl {
    if (apiBaseUrl.isEmpty) return '/hubs/tracking';
    final base = apiBaseUrl.endsWith('/')
        ? apiBaseUrl.substring(0, apiBaseUrl.length - 1)
        : apiBaseUrl;
    return '$base/hubs/tracking';
  }

  static const bool isDevelopment = bool.fromEnvironment(
    'DEBUG',
    defaultValue: true,
  );

  static const bool enableHttpLogging = bool.fromEnvironment(
    'HTTP_LOGGING',
    defaultValue: true,
  );

  static const Duration connectTimeout = Duration(seconds: 15);
  static const Duration receiveTimeout = Duration(seconds: 15);
  static const int gpsDistanceFilter = 10;
  static const int gpsUpdateIntervalSeconds = 5;
  static const int offerCountdownSeconds = 30;
}
