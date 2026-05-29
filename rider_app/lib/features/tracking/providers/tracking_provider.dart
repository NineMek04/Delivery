import 'dart:async';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../../core/api/services/order_api_service.dart';
import '../../../core/signalr/signalr_service.dart';
import '../../../models/order.dart';

final activeOrderProvider = NotifierProvider<ActiveOrderNotifier, ActiveOrderState>(
  ActiveOrderNotifier.new,
);

class ActiveOrderNotifier extends Notifier<ActiveOrderState> {
  StreamSubscription? _statusSubscription;
  StreamSubscription? _locationSubscription;

  @override
  ActiveOrderState build() {
    ref.onDispose(() {
      _statusSubscription?.cancel();
      _locationSubscription?.cancel();
    });
    return const ActiveOrderState();
  }

  Future<void> watchOrder(String orderId) async {
    state = state.copyWith(isLoading: true, error: null);

    try {
      final order = await ref.read(orderApiServiceProvider).getById(orderId);
      state = state.copyWith(isLoading: false, order: order);

      // Listen for SignalR updates
      _statusSubscription?.cancel();
      _statusSubscription = ref.read(signalRServiceProvider.notifier).onOrderStatusChanged.listen((event) {
        if (event.orderId == orderId) {
          _refreshOrder();
        }
      });

      _locationSubscription?.cancel();
      _locationSubscription = ref.read(signalRServiceProvider.notifier).onRiderLocationUpdated.listen((event) {
        if (state.order?.assignedRiderId == event.riderId) {
          state = state.copyWith(riderLat: event.latitude, riderLng: event.longitude);
        }
      });
      
    } catch (e) {
      state = state.copyWith(isLoading: false, error: e.toString());
    }
  }

  Future<void> _refreshOrder() async {
    if (state.order == null) return;
    try {
      final order = await ref.read(orderApiServiceProvider).getById(state.order!.id);
      state = state.copyWith(order: order);
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
