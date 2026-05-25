import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:logger/logger.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../../models/dispatch_offer.dart';
import '../auth/auth_service.dart';
import '../config/environment.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

/// Order status change from SignalR `OrderStatusChanged`.
class OrderStatusChangedEvent {
  final String orderId;
  final String status;

  const OrderStatusChangedEvent({
    required this.orderId,
    required this.status,
  });
}

/// Result from `OfferAcceptedResult` hub callback.
class OfferAcceptedResult {
  final bool success;
  final String? message;

  const OfferAcceptedResult({required this.success, this.message});
}

/// Result from `RiderStatusUpdatedResult` hub callback.
class RiderStatusResult {
  final bool success;
  final String? status;
  final String? message;

  const RiderStatusResult({
    required this.success,
    this.status,
    this.message,
  });
}

/// SignalR client for TrackingHub (`/hubs/tracking`).
class SignalRService extends Notifier<SignalRConnectionState> {
  HubConnection? _hubConnection;

  final _offerController = StreamController<DispatchOffer>.broadcast();
  final _orderStatusController =
      StreamController<OrderStatusChangedEvent>.broadcast();
  final _offerAcceptedController =
      StreamController<OfferAcceptedResult>.broadcast();
  final _riderStatusResultController =
      StreamController<RiderStatusResult>.broadcast();

  @override
  SignalRConnectionState build() {
    ref.onDispose(() {
      _hubConnection?.stop();
      _offerController.close();
      _orderStatusController.close();
      _offerAcceptedController.close();
      _riderStatusResultController.close();
    });
    return SignalRConnectionState.disconnected;
  }

  Stream<DispatchOffer> get onOfferReceived => _offerController.stream;

  Stream<OrderStatusChangedEvent> get onOrderStatusChanged =>
      _orderStatusController.stream;

  Stream<OfferAcceptedResult> get onOfferAcceptedResult =>
      _offerAcceptedController.stream;

  Stream<RiderStatusResult> get onRiderStatusResult =>
      _riderStatusResultController.stream;

  Future<void> connect() async {
    if (state == SignalRConnectionState.connected ||
        state == SignalRConnectionState.connecting) {
      return;
    }

    await disconnect();

    final authService = ref.read(authServiceProvider.notifier);

    _hubConnection = HubConnectionBuilder()
        .withUrl(
          Environment.signalRUrl,
          options: HttpConnectionOptions(
            accessTokenFactory: () async =>
                authService.currentToken ?? '',
          ),
        )
        .withAutomaticReconnect(retryDelays: [0, 2000, 10000, 30000])
        .build();

    _registerHandlers();

    _hubConnection!.onclose(({error}) {
      _logger.w('SignalR disconnected', error: error);
      state = SignalRConnectionState.disconnected;
    });

    _hubConnection!.onreconnecting(({error}) {
      _logger.i('SignalR reconnecting...', error: error);
      state = SignalRConnectionState.reconnecting;
    });

    _hubConnection!.onreconnected(({connectionId}) {
      _logger.i('SignalR reconnected: $connectionId');
      state = SignalRConnectionState.connected;
    });

    try {
      state = SignalRConnectionState.connecting;
      await _hubConnection!.start();
      state = SignalRConnectionState.connected;
      _logger.i('SignalR connected to ${Environment.signalRUrl}');
    } catch (e) {
      state = SignalRConnectionState.error;
      _logger.e('SignalR connection failed', error: e);
      rethrow;
    }
  }

  Future<void> disconnect() async {
    await _hubConnection?.stop();
    _hubConnection = null;
    state = SignalRConnectionState.disconnected;
  }

  /// Hub: UpdateLocation(lat, lng, accuracy)
  Future<void> sendLocationUpdate({
    required double lat,
    required double lng,
    double accuracy = 10.0,
  }) async {
    if (state != SignalRConnectionState.connected) return;

    try {
      await _hubConnection!.invoke(
        'UpdateLocation',
        args: [lat, lng, accuracy],
      );
    } catch (e) {
      _logger.e('Failed to send location update', error: e);
    }
  }

  /// Hub: UpdateStatus(status) — AVAILABLE maps to IDLE on server.
  Future<bool> updateStatus(String status) async {
    if (state != SignalRConnectionState.connected) return false;

    try {
      final result = await _hubConnection!.invoke('UpdateStatus', args: [status]);
      return result == true;
    } catch (e) {
      _logger.e('Failed to update rider status', error: e);
      return false;
    }
  }

  /// Hub: AcceptOffer(offerId, version)
  Future<void> acceptOffer({
    required String offerId,
    required int version,
  }) async {
    if (state != SignalRConnectionState.connected) return;
    await _hubConnection!.invoke('AcceptOffer', args: [offerId, version]);
  }

  /// Hub: RejectOffer(offerId, orderId)
  Future<void> rejectOffer({
    required String offerId,
    required String orderId,
  }) async {
    if (state != SignalRConnectionState.connected) return;
    await _hubConnection!.invoke('RejectOffer', args: [offerId, orderId]);
  }

  /// Hub: UpdateHeartbeat — keep presence alive + state sync after reconnect.
  Future<void> sendHeartbeat() async {
    if (state != SignalRConnectionState.connected) return;
    try {
      await _hubConnection!.invoke('UpdateHeartbeat');
    } catch (e) {
      _logger.w('Heartbeat failed', error: e);
    }
  }

  void _registerHandlers() {
    final hub = _hubConnection!;

    hub.on('OfferReceived', (args) {
      if (args == null || args.isEmpty) return;
      try {
        final offer = DispatchOffer.fromJson(_asJsonMap(args.first));
        _offerController.add(offer);
        _logger.i('Offer received: ${offer.offerId}');
      } catch (e) {
        _logger.e('Failed to parse OfferReceived', error: e);
      }
    });

    hub.on('OrderStatusChanged', (args) {
      if (args == null || args.length < 2) return;
      final orderId = args[0]?.toString() ?? '';
      final status = args[1]?.toString() ?? '';
      if (orderId.isEmpty) return;
      _orderStatusController.add(
        OrderStatusChangedEvent(orderId: orderId, status: status),
      );
    });

    hub.on('OfferAcceptedResult', (args) {
      if (args == null || args.isEmpty) return;
      try {
        final map = _asJsonMap(args.first);
        _offerAcceptedController.add(
          OfferAcceptedResult(
            success: map['Success'] == true || map['success'] == true,
            message: map['Message']?.toString() ?? map['message']?.toString(),
          ),
        );
      } catch (e) {
        _logger.e('Failed to parse OfferAcceptedResult', error: e);
      }
    });

    hub.on('RiderStatusUpdatedResult', (args) {
      if (args == null || args.isEmpty) return;
      try {
        final map = _asJsonMap(args.first);
        _riderStatusResultController.add(
          RiderStatusResult(
            success: map['Success'] == true || map['success'] == true,
            status: map['Status']?.toString() ?? map['status']?.toString(),
            message: map['Message']?.toString() ?? map['message']?.toString(),
          ),
        );
      } catch (e) {
        _logger.e('Failed to parse RiderStatusUpdatedResult', error: e);
      }
    });
  }

  Map<String, dynamic> _asJsonMap(Object? value) {
    if (value is Map<String, dynamic>) return value;
    if (value is Map) return Map<String, dynamic>.from(value);
    throw FormatException('Expected map payload, got $value');
  }
}

enum SignalRConnectionState {
  disconnected,
  connecting,
  connected,
  reconnecting,
  error,
}

final signalRServiceProvider =
    NotifierProvider<SignalRService, SignalRConnectionState>(
  SignalRService.new,
);
