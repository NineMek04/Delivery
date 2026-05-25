---
scope: Flutter Rider Mobile App
source_of_truth:
  - AI-BLUEPRINT.md (Sections 9.1-9.5, Flutter Foundation & Feature List)
  - PROJECT-SPEC.md (Section 10, Flutter status)
  - AI-CHANGELOG.md (2026-05-14 Flutter foundation, 2026-05-14 AuthService, 2026-05-15 Background GPS)
  - rider_app/lib/ (codebase)
related_contexts:
  - .docs/ai-context/contracts/signalr-contracts.md
  - .docs/ai-context/contracts/state-machine.md
  - .docs/ai-context/spec-backend.md
forbidden_patterns:
  - ใช้ http.dart แทน Dio
  - ใช้ setState โดยตรง (ต้องผ่าน Riverpod providers)
  - ไม่ handle 401 ผ่าน ErrorInterceptor
  - ใช้ GPS ที่ accuracy > 50m (ต้องกรองก่อน)
known_pitfalls:
  - Token Clocking race condition → ใช้ _isRefreshing flag ป้องกัน
  - GPS drift: accuracy > 50m → ต้องกรองออก (noise filter)
  - Background GPS iOS: ต้องตั้ง allowBackgroundLocationUpdates + showBackgroundLocationIndicator
  - Background GPS Android: ต้องเปิด Foreground Service + persistent notification
  - SignalR reconnect: Rider อาจส่ง GPS ก่อน reconnect เสร็จ
  - build_runner ต้องรันก่อนใช้ .g.dart files
---

# spec-mobile-rider.md — Flutter Rider Mobile App

> **Source**: `AI-BLUEPRINT.md` Sec 9 + `PROJECT-SPEC.md` + `AI-CHANGELOG.md`  
> **For SignalR event contracts** → `contracts/signalr-contracts.md`  
> **For Order/Rider states** → `contracts/state-machine.md`

---

## 1. Tech Stack & Foundation Status

| Package | Purpose | Status |
|---|---|---|
| `dio` | HTTP Client | ✅ Ready |
| `riverpod` | State Management | ✅ Ready |
| `go_router` | Navigation | ✅ Ready |
| `signalr_netcore` | SignalR WebSocket | ✅ Ready |
| `location` | GPS (foreground + background) | ✅ Ready |
| `flutter_secure_storage` | Token storage | ✅ Ready |
| `jwt_decoder` | JWT parsing | ✅ Ready |
| `flutter_map` | Map rendering (OpenStreetMap) | ✅ Ready |

---

## 2. Foundation File Structure

```
rider_app/lib/
├── app/
│   └── app_router.dart          ← GoRouter + AuthGuard + MainShell (4 tabs)
├── core/
│   ├── auth/
│   │   └── auth_service.dart    ← JWT + Refresh Token + Token Clocking
│   ├── signalr/
│   │   └── signalr_service.dart ← SignalR connect/disconnect/sendGPS
│   ├── location/
│   │   └── location_service.dart ← Background GPS + Noise Filter
│   └── api/
│       └── delivery_api_client.dart ← Dio + Auth/Error Interceptors
└── features/
    ├── auth/screens/login_screen.dart        🟡 Placeholder
    ├── home/screens/home_screen.dart          🟡 Placeholder
    ├── delivery/screens/
    │   ├── active_delivery_screen.dart        🟡 Placeholder
    │   └── delivery_history_screen.dart       🟡 Placeholder
    └── tracking/screens/map_tracking_screen.dart 🟡 Placeholder (flutter_map มีแล้ว)
```

---

## 3. AuthService — Token Management

```dart
// auth_service.dart key behaviors

// Token Clocking: proactive refresh every 30s
Timer.periodic(Duration(seconds: 30), (_) async {
  if (isAccessTokenExpiringSoon()) {  // < 2 minutes remaining
    await refreshAccessToken();
  }
});

// Concurrent refresh protection
bool _isRefreshing = false;

Future<void> refreshAccessToken() async {
  if (_isRefreshing) return;  // prevent race condition
  _isRefreshing = true;
  try {
    final response = await _dio.post('/api/v1/auth/refresh',
      data: { 'refreshToken': await getRefreshToken() }
    );
    await setTokens(response.data['accessToken'], response.data['refreshToken']);
  } finally {
    _isRefreshing = false;
  }
}

// Malformed token handling
bool isAccessTokenExpiringSoon() {
  try {
    final token = getAccessToken();
    if (token == null) return true;
    final expiry = JwtDecoder.getExpirationDate(token);
    return expiry.difference(DateTime.now()).inMinutes < 2;
  } catch (_) {
    return true;  // treat malformed token as expired
  }
}
```

---

## 4. ErrorInterceptor — Auto Refresh & Retry

```dart
// api_interceptors.dart
onError: (DioError error, handler) async {
  if (error.response?.statusCode == 401) {
    // ห้าม retry สำหรับ auth endpoints (loop prevention)
    if (error.requestOptions.path.contains('/auth/')) {
      return handler.next(error);
    }
    await authService.refreshAccessToken();
    // Retry original request with new token
    final retryResponse = await dio.fetch(error.requestOptions);
    return handler.resolve(retryResponse);
  }
  return handler.next(error);
}
```

---

## 5. LocationService — Background GPS + Noise Filter

```dart
// location_service.dart

// Noise filter: discard GPS points with accuracy > 50m
void _handleLocationUpdate(LocationData data) {
  if (data.accuracy != null && data.accuracy! > 50.0) {
    return;  // GPS drift protection
  }
  _locationController.add(data);
  _sendToSignalR(data.latitude!, data.longitude!);
}

// Android: Foreground Service with persistent notification
await location.enableBackgroundMode(enable: true);
// นำ user รู้ว่า GPS กำลัง active ผ่าน notification

// iOS: Background location
await location.changeSettings(
  accuracy: LocationAccuracy.high,
  interval: 5000,  // ms
  distanceFilter: 10,  // meters
);
```

---

## 6. SignalRService — Connection & GPS Sending

```dart
// signalr_service.dart

// Connect (ส่ง JWT ผ่าน QueryString สำหรับ WebSocket)
final connection = HubConnectionBuilder()
  .withUrl('${baseUrl}/hubs/tracking',
    options: HttpConnectionOptions(
      accessTokenFactory: () async => await authService.getAccessToken(),
    ))
  .withAutomaticReconnect(retryDelays: [0, 2000, 10000, 30000])
  .build();

// Send GPS
await connection.invoke('UpdateLocation', args: [
  latitude,
  longitude,
  accuracy,  // meters
]);

// Listen for offers
connection.on('OnOfferReceived', (args) {
  final offer = args![0] as Map<String, dynamic>;
  // trigger OfferBottomSheet
});

// Accept/Reject
await connection.invoke('AcceptOffer', args: [offerId, offerVersion]);
await connection.invoke('RejectOffer', args: [offerId, orderId]);
```

---

## 7. Riverpod State Structure

```dart
// Required providers (ต้องสร้าง)
final orderProvider = FutureProvider<List<OrderDto>>(...);
final activeOrderProvider = StateProvider<OrderDto?>(...);
final riderStatusProvider = StateProvider<RiderState>(...);
final incomingOfferProvider = StateProvider<OfferDto?>(...);
final orderHistoryProvider = FutureProvider<List<OrderDto>>(...);
```

---

## 8. Rider App Feature Priority (Execution Order)

```
Step 1  Login Screen         🔴 Must Have — AuthService เชื่อมแล้ว
Step 2  Home Screen          🔴 Must Have — Toggle Online/Offline + SignalR
Step 3  OfferBottomSheet     🔴 Must Have — Countdown 30s + Accept/Reject
Step 4  Active Delivery      🔴 Must Have — Order detail + Status buttons
Step 5  Map Tracking         🔴 Must Have — GPS marker + Polyline + Auto-follow
Step 6  Delivery History     🟡 Should Have
Step 7  Profile Screen       🟡 Should Have
Step 8  Shared Components    🔴 LoadingOverlay, ErrorSnackBar, StatusBadge
```

---

## 9. Shared Components (ต้องสร้าง)

| Component | ใช้ที่ | Priority |
|---|---|---|
| `LoadingOverlay` | ทุกหน้าที่รอ API | 🔴 |
| `ErrorSnackBar` | แสดง error | 🔴 |
| `StatusBadge` | Order/Rider status พร้อมสี | 🔴 |
| `OfferBottomSheet` | Home Screen — popup รับ/ปฏิเสธ + countdown | 🔴 |
| `CountdownTimer` | ใน OfferBottomSheet | 🔴 |
| `ConfirmDialog` | Logout / ปฏิเสธงาน | 🔴 |
| `ConnectionStatusBar` | แสดงสถานะ SignalR | 🟡 |

---

## 10. Required Endpoints (Backend → Rider App)

| Method | Endpoint | Purpose |
|---|---|---|
| POST | `/api/v1/auth/login` | Login |
| POST | `/api/v1/auth/refresh` | Refresh token |
| POST | `/api/v1/auth/logout` | Logout |
| GET | `/api/v1/orders/my` | My assigned orders |
| PATCH | `/api/v1/orders/{id}/status` | Update order status |
| WebSocket | `/hubs/tracking` | SignalR GPS + Offers |
