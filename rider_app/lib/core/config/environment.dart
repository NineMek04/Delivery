/// Environment configuration for the Rider App.
///
/// เทียบกับ:
/// - Angular: `admin-dashboard/src/environments/environment.ts`
/// - .NET: `BackendApi/appsettings.json`
///
/// NOTE: 10.0.2.2 = localhost mapping สำหรับ Android Emulator
/// ถ้ารันบนอุปกรณ์จริง → เปลี่ยนเป็น IP จริงของเครื่อง dev
class Environment {
  Environment._();

  // ── API Configuration ──────────────────────────────────────────────
  /// Base URL ของ Backend API (ตรงกับ port ใน docker-compose.yml)
  static const String apiBaseUrl = 'http://10.0.2.2:5000';

  /// API path prefix (ตรงกับ route prefix ใน BackendApi `api/v1/[controller]`)
  static const String apiPrefix = '/api/v1';

  /// Full API URL
  static String get apiUrl => '$apiBaseUrl$apiPrefix';

  // ── SignalR Configuration ──────────────────────────────────────────
  /// SignalR Hub URL (ตรงกับ Hub mapping ใน BackendApi)
  static const String signalRUrl = 'http://10.0.2.2:5000/hubs/tracking';

  // ── Feature Flags ──────────────────────────────────────────────────
  /// ใช้ระบุ environment ปัจจุบัน
  static const bool isDevelopment = true;

  /// เปิด/ปิด logging ของ HTTP requests
  static const bool enableHttpLogging = true;

  // ── Timeouts ───────────────────────────────────────────────────────
  /// Connection timeout สำหรับ HTTP requests
  static const Duration connectTimeout = Duration(seconds: 15);

  /// Receive timeout สำหรับ HTTP requests
  static const Duration receiveTimeout = Duration(seconds: 15);

  // ── GPS Configuration ──────────────────────────────────────────────
  /// ระยะห่างขั้นต่ำ (เมตร) ก่อนส่ง GPS update ใหม่
  static const int gpsDistanceFilter = 10;

  /// ความถี่ในการส่ง GPS update ผ่าน SignalR (วินาที)
  static const int gpsUpdateIntervalSeconds = 5;
}
