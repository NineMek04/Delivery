import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:logger/logger.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../auth/auth_service.dart';
import '../config/environment.dart';
import 'signalr_service.dart' show JitteredRetryPolicy;

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

class CustomerOrderStatusChangedEvent {
  final String orderId;
  final String status;

  const CustomerOrderStatusChangedEvent({
    required this.orderId,
    required this.status,
  });
}

enum CustomerSignalRState {
  disconnected,
  connecting,
  connected,
  reconnecting,
  error,
}

class CustomerSignalRService extends Notifier<CustomerSignalRState> {
  HubConnection? _hubConnection;
  final _orderStatusController =
      StreamController<CustomerOrderStatusChangedEvent>.broadcast();

  @override
  CustomerSignalRState build() {
    ref.onDispose(() {
      _hubConnection?.stop();
      _orderStatusController.close();
    });
    return CustomerSignalRState.disconnected;
  }

  Stream<CustomerOrderStatusChangedEvent> get onOrderStatusChanged =>
      _orderStatusController.stream;

  Future<void> connect() async {
    if (state == CustomerSignalRState.connected ||
        state == CustomerSignalRState.connecting) {
      return;
    }

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
      _logger.w('[CustomerSignalR] Disconnected', error: error);
      state = CustomerSignalRState.disconnected;
    });
    _hubConnection!.onreconnecting(({error}) {
      _logger.i('[CustomerSignalR] Reconnecting', error: error);
      state = CustomerSignalRState.reconnecting;
    });
    _hubConnection!.onreconnected(({connectionId}) {
      _logger.i('[CustomerSignalR] Reconnected: $connectionId');
      state = CustomerSignalRState.connected;
    });

    try {
      state = CustomerSignalRState.connecting;
      await _hubConnection!.start();
      state = CustomerSignalRState.connected;
    } catch (error) {
      state = CustomerSignalRState.error;
      _logger.e('[CustomerSignalR] Connection failed', error: error);
      rethrow;
    }
  }

  Future<void> disconnect() async {
    await _hubConnection?.stop();
    _hubConnection = null;
    state = CustomerSignalRState.disconnected;
  }

  void _registerHandlers() {
    _hubConnection!.on('OrderStatusChanged', (args) {
      if (args == null || args.isEmpty) return;

      var orderId = '';
      var status = '';
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
          orderId =
              map['orderId']?.toString() ?? map['OrderId']?.toString() ?? '';
          status = map['newStatus']?.toString() ??
              map['NewStatus']?.toString() ??
              map['status']?.toString() ??
              map['Status']?.toString() ??
              '';
        }
      }

      if (orderId.isEmpty || status.isEmpty) return;
      _orderStatusController.add(
        CustomerOrderStatusChangedEvent(orderId: orderId, status: status),
      );
    });
  }
}

final customerSignalRServiceProvider =
    NotifierProvider<CustomerSignalRService, CustomerSignalRState>(
  CustomerSignalRService.new,
);
