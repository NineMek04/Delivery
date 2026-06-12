/// App-wide constants for the Rider App.
///
/// เทียบกับ:
/// - .NET: `BackendApi/Security/AuthConstants.cs`
class AppConstants {
  AppConstants._();

  // ── App Info ────────────────────────────────────────────────────────
  static const String appName = 'Rider App';
  static const String appVersion = '1.0.0';

  // ── Storage Keys ───────────────────────────────────────────────────
  /// Key สำหรับเก็บ JWT access token ใน SecureStorage
  static const String accessTokenKey = 'delivery_access_token';

  /// Key สำหรับเก็บ refresh token (ถ้ามีในอนาคต)
  static const String refreshTokenKey = 'delivery_refresh_token';

  /// Key สำหรับเก็บ user data
  static const String userDataKey = 'delivery_user_data';

  // ── Rider Status ───────────────────────────────────────────────────
  /// ตรงกับ Rider.Status ใน BackendApi/Models/Rider.cs
  static const String statusAvailable = 'IDLE';
  static const String statusReserved = 'RESERVED';
  static const String statusBusy = 'BUSY';
  static const String statusStale = 'STALE';
  static const String statusOffline = 'OFFLINE';

  // ── Order Status ───────────────────────────────────────────────────
  /// ตรงกับ Order.Status ใน BackendApi/Models/Order.cs
  static const String orderAssigned = 'ASSIGNED';
  static const String orderDelivering = 'DELIVERING';
  static const String orderCompleted = 'COMPLETED';
  static const String orderCancelled = 'CANCELLED';

  // ── API Endpoints (relative to baseUrl /api/v1 — ห้ามขึ้นต้นด้วย /) ──
  /// Dio รวม path กับ baseUrl; path ที่ขึ้นต้นด้วย / จะหลุดออกจาก /api/v1
  static const String ridersEndpoint = 'riders';
  static const String ordersEndpoint = 'orders';
  static const String authEndpoint = 'auth';
}
