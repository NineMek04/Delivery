import 'package:logger/logger.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:signalr_netcore/signalr_netcore.dart';

import '../auth/auth_service.dart';
import '../config/environment.dart';

part 'signalr_service.g.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

/// SignalR Service — Real-time communication กับ BackendApi Hub.
///
/// เทียบกับ:
/// - .NET: SignalR Hub ที่ register ใน BackendApi (Setup/ServiceSetup.cs → `AddSignalR()`)
/// - Data Flow: Flutter App ──(SignalR)──► .NET Backend
///
/// ใช้สำหรับ:
/// 1. ส่ง GPS location updates ของ Rider → Backend (real-time)
/// 2. รับ route updates / order assignments จาก Backend
/// 3. Broadcast notifications
///
/// ```dart
/// // Usage:
/// final signalR = ref.watch(signalRServiceProvider.notifier);
/// await signalR.connect();
/// signalR.sendLocationUpdate(lat: 13.7563, lng: 100.5018);
/// ```
@riverpod
class SignalRService extends _$SignalRService {
  HubConnection? _hubConnection;

  @override
  SignalRConnectionState build() {
    ref.onDispose(() {
      _hubConnection?.stop();
    });
    return SignalRConnectionState.disconnected;
  }

  /// เชื่อมต่อ SignalR Hub.
  Future<void> connect() async {
    if (_hubConnection != null) return;

    final authService = ref.read(authServiceProvider.notifier);
    final token = authService.currentToken;

    _hubConnection = HubConnectionBuilder()
        .withUrl(
          Environment.signalRUrl,
          options: HttpConnectionOptions(
            accessTokenFactory: () async => token ?? '',
          ),
        )
        .withAutomaticReconnect()
        .build();

    // ── Register Hub event handlers ──────────────────────────────────

    // รับ route update จาก Backend
    _hubConnection!.on('ReceiveRouteUpdate', _onReceiveRouteUpdate);

    // รับ order assignment ใหม่
    _hubConnection!.on('ReceiveOrderAssignment', _onReceiveOrderAssignment);

    // รับ broadcast message
    _hubConnection!.on('ReceiveMessage', _onReceiveMessage);

    // ── Connection lifecycle ─────────────────────────────────────────
    _hubConnection!.onclose(({error}) {
      _logger.w('📡 SignalR disconnected', error: error);
      state = SignalRConnectionState.disconnected;
    });

    _hubConnection!.onreconnecting(({error}) {
      _logger.i('🔄 SignalR reconnecting...', error: error);
      state = SignalRConnectionState.reconnecting;
    });

    _hubConnection!.onreconnected(({connectionId}) {
      _logger.i('✅ SignalR reconnected: $connectionId');
      state = SignalRConnectionState.connected;
    });

    // ── Start connection ─────────────────────────────────────────────
    try {
      state = SignalRConnectionState.connecting;
      await _hubConnection!.start();
      state = SignalRConnectionState.connected;
      _logger.i('✅ SignalR connected to ${Environment.signalRUrl}');
    } catch (e) {
      state = SignalRConnectionState.error;
      _logger.e('❌ SignalR connection failed', error: e);
    }
  }

  /// ยกเลิกการเชื่อมต่อ.
  Future<void> disconnect() async {
    await _hubConnection?.stop();
    _hubConnection = null;
    state = SignalRConnectionState.disconnected;
  }

  /// ส่ง GPS location update ไปยัง Backend Hub.
  ///
  /// Data Flow: Flutter → SignalR → .NET Backend → PostgreSQL/PostGIS
  Future<void> sendLocationUpdate({
    required double lat,
    required double lng,
  }) async {
    if (state != SignalRConnectionState.connected) return;

    try {
      await _hubConnection!.invoke(
        'UpdateRiderLocation',
        args: [lat, lng],
      );
    } catch (e) {
      _logger.e('❌ Failed to send location update', error: e);
    }
  }

  /// อัปเดตสถานะ Rider (AVAILABLE, DELIVERING, OFFLINE).
  Future<void> updateRiderStatus(String status) async {
    if (state != SignalRConnectionState.connected) return;

    try {
      await _hubConnection!.invoke(
        'UpdateRiderStatus',
        args: [status],
      );
    } catch (e) {
      _logger.e('❌ Failed to update rider status', error: e);
    }
  }

  // ── Hub event handlers ───────────────────────────────────────────

  void _onReceiveRouteUpdate(List<Object?>? arguments) {
    _logger.i('📍 Received route update: $arguments');
    // TODO: Parse route waypoints และอัปเดต state
  }

  void _onReceiveOrderAssignment(List<Object?>? arguments) {
    _logger.i('📦 Received order assignment: $arguments');
    // TODO: Parse order data และแสดง notification
  }

  void _onReceiveMessage(List<Object?>? arguments) {
    _logger.i('💬 Received message: $arguments');
    // TODO: แสดง notification
  }
}

/// สถานะการเชื่อมต่อ SignalR.
enum SignalRConnectionState {
  disconnected,
  connecting,
  connected,
  reconnecting,
  error,
}
