import 'dart:async';
import 'dart:math' as math;

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/api/services/order_api_service.dart';
import '../../../core/signalr/customer_signalr_service.dart';
import '../../../models/order.dart';

final activeOrderProvider =
    NotifierProvider<ActiveOrderNotifier, ActiveOrderState>(
  ActiveOrderNotifier.new,
);

class ActiveOrderNotifier extends Notifier<ActiveOrderState> {
  StreamSubscription<CustomerOrderStatusChangedEvent>? _statusSubscription;
  StreamSubscription<CustomerRiderLocationUpdatedEvent>? _locationSubscription;
  StreamSubscription<void>? _reconnectedSubscription;
  String? _watchedOrderId;

  @override
  ActiveOrderState build() {
    ref.onDispose(() {
      _statusSubscription?.cancel();
      _locationSubscription?.cancel();
      _reconnectedSubscription?.cancel();
    });
    return const ActiveOrderState();
  }

  Future<void> watchOrder(String orderId) async {
    _watchedOrderId = orderId;
    state = const ActiveOrderState(isLoading: true);

    await _statusSubscription?.cancel();
    await _locationSubscription?.cancel();
    await _reconnectedSubscription?.cancel();

    try {
      final order = await ref.read(orderApiServiceProvider).getById(orderId);
      if (_watchedOrderId != orderId) return;

      state = ActiveOrderState(order: order);

      final signalR = ref.read(customerSignalRServiceProvider.notifier);
      await signalR.connect();
      if (_watchedOrderId != orderId) return;

      _statusSubscription = signalR.onOrderStatusChanged.listen((event) {
        if (event.orderId == orderId) {
          _refreshOrder();
        }
      });
      _locationSubscription = signalR.onRiderLocationUpdated.listen((event) {
        final assignedRiderId = state.order?.assignedRiderId;
        if (assignedRiderId != null && event.riderId == assignedRiderId) {
          state = state.copyWith(
            riderLat: event.latitude,
            riderLng: event.longitude,
          );
        }
      });
      _reconnectedSubscription = signalR.onReconnected.listen((_) {
        unawaited(_refreshAfterReconnect());
      });
    } catch (e) {
      if (_watchedOrderId == orderId) {
        state = ActiveOrderState(error: e.toString());
      }
    }
  }

  Future<void> _refreshAfterReconnect() async {
    final delayMs = 500 + math.Random().nextInt(3000);
    await Future<void>.delayed(Duration(milliseconds: delayMs));
    await _refreshOrder();
  }

  Future<void> _refreshOrder() async {
    final orderId = state.order?.id;
    if (orderId == null || _watchedOrderId != orderId) return;

    try {
      final order = await ref.read(orderApiServiceProvider).getById(orderId);
      if (_watchedOrderId == orderId) {
        final riderChanged =
            state.order?.assignedRiderId != order.assignedRiderId;
        state = riderChanged
            ? ActiveOrderState(order: order)
            : state.copyWith(order: order);
      }
    } catch (_) {}
  }
}

class ActiveOrderState {
  final bool isLoading;
  final String? error;
  final OrderDto? order;
  final double? riderLat;
  final double? riderLng;

  const ActiveOrderState({
    this.isLoading = false,
    this.error,
    this.order,
    this.riderLat,
    this.riderLng,
  });

  ActiveOrderState copyWith({
    bool? isLoading,
    String? error,
    OrderDto? order,
    double? riderLat,
    double? riderLng,
  }) {
    return ActiveOrderState(
      isLoading: isLoading ?? this.isLoading,
      error: error,
      order: order ?? this.order,
      riderLat: riderLat ?? this.riderLat,
      riderLng: riderLng ?? this.riderLng,
    );
  }
}
