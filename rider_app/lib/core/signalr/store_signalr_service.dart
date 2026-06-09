import 'dart:async';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:logger/logger.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../auth/auth_service.dart';
import '../config/environment.dart';
import 'signalr_service.dart' show JitteredRetryPolicy;
import '../../models/order.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

/// Incoming order notification from "OrderCreated" SignalR event.
class StoreOrderCreatedEvent {
  final OrderDto order;
  const StoreOrderCreatedEvent(this.order);
}

/// Status changed notification for an order the store cares about.
class StoreOrderStatusChangedEvent {
  final String orderId;
  final String status;
  const StoreOrderStatusChangedEvent({required this.orderId, required this.status});
}

/// SignalR connection state for the store.
enum StoreSignalRState { disconnected, connecting, connected, reconnecting, error }

/// StoreSignalRService — connects store partners to TrackingHub and
/// broadcasts OrderCreated / OrderStatusChanged events to the Store UI.
class StoreSignalRService extends Notifier<StoreSignalRState> {
  HubConnection? _hubConnection;

  final _orderCreatedController = StreamController<StoreOrderCreatedEvent>.broadcast();
  final _orderStatusController = StreamController<StoreOrderStatusChangedEvent>.broadcast();

  @override
  StoreSignalRState build() {
    ref.onDispose(() {
      _hubConnection?.stop();
      _orderCreatedController.close();
      _orderStatusController.close();
    });
    return StoreSignalRState.disconnected;
  }

  Stream<StoreOrderCreatedEvent> get onOrderCreated => _orderCreatedController.stream;
  Stream<StoreOrderStatusChangedEvent> get onOrderStatusChanged => _orderStatusController.stream;

  Future<void> connect() async {
    if (state == StoreSignalRState.connected || state == StoreSignalRState.connecting) return;

    await disconnect();

    final authService = ref.read(authServiceProvider.notifier);

    _hubConnection = HubConnectionBuilder()
        .withUrl(
          Environment.signalRUrl,
          options: HttpConnectionOptions(
            accessTokenFactory: () async => authService.currentToken ?? '',
          ),
        )
        .withAutomaticReconnect(reconnectPolicy: JitteredRetryPolicy())
        .build();

    _registerHandlers();

    _hubConnection!.onclose(({error}) {
      _logger.w('[StoreSignalR] Disconnected', error: error);
      state = StoreSignalRState.disconnected;
    });

    _hubConnection!.onreconnecting(({error}) {
      _logger.i('[StoreSignalR] Reconnecting...', error: error);
      state = StoreSignalRState.reconnecting;
    });

    _hubConnection!.onreconnected(({connectionId}) {
      _logger.i('[StoreSignalR] Reconnected: $connectionId');
      state = StoreSignalRState.connected;
    });

    try {
      state = StoreSignalRState.connecting;
      await _hubConnection!.start();
      state = StoreSignalRState.connected;
      _logger.i('[StoreSignalR] Connected to ${Environment.signalRUrl}');
    } catch (e) {
      state = StoreSignalRState.error;
      _logger.e('[StoreSignalR] Connection failed', error: e);
      rethrow;
    }
  }

  Future<void> disconnect() async {
    await _hubConnection?.stop();
    _hubConnection = null;
    state = StoreSignalRState.disconnected;
  }

  void _registerHandlers() {
    final hub = _hubConnection!;

    // Backend sends "OrderCreated" with the full OrderDto as the first argument
    hub.on('OrderCreated', (args) {
      if (args == null || args.isEmpty) return;
      try {
        final raw = args.first;
        final Map<String, dynamic> map;
        if (raw is Map<String, dynamic>) {
          map = raw;
        } else if (raw is Map) {
          map = Map<String, dynamic>.from(raw);
        } else {
          _logger.w('[StoreSignalR] OrderCreated: unexpected payload type ${raw.runtimeType}');
          return;
        }
        final order = OrderDto.fromJson(map);
        _orderCreatedController.add(StoreOrderCreatedEvent(order));
        _logger.i('[StoreSignalR] OrderCreated received: orderId=${order.id}');
      } catch (e) {
        _logger.e('[StoreSignalR] Failed to parse OrderCreated payload', error: e);
      }
    });

    hub.on('OrderStatusChanged', (args) {
      if (args == null || args.isEmpty) return;
      try {
        String orderId = '';
        String status = '';
        if (args.length >= 2) {
          orderId = args[0]?.toString() ?? '';
          status = args[1]?.toString() ?? '';
        } else {
          final raw = args.first;
          final map = raw is Map<String, dynamic>
              ? raw
              : raw is Map
                  ? Map<String, dynamic>.from(raw)
                  : null;
          if (map != null) {
            orderId = map['orderId']?.toString() ?? map['OrderId']?.toString() ?? '';
            status = map['newStatus']?.toString() ??
                map['NewStatus']?.toString() ??
                map['status']?.toString() ??
                map['Status']?.toString() ??
                '';
          }
        }
        if (orderId.isEmpty || status.isEmpty) return;
        _orderStatusController.add(StoreOrderStatusChangedEvent(orderId: orderId, status: status));
      } catch (e) {
        _logger.e('[StoreSignalR] Failed to parse OrderStatusChanged', error: e);
      }
    });
  }
}

final storeSignalRServiceProvider =
    NotifierProvider<StoreSignalRService, StoreSignalRState>(
  StoreSignalRService.new,
);
