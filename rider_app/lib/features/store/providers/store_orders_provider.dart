import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/api/services/order_api_service.dart';
import '../../../core/signalr/store_signalr_service.dart';
import '../../../models/order.dart';

// ─────────────────────────────────────────────────────────────────────────────
// StoreOrdersState — holds the list of incoming/active orders for the store
// ─────────────────────────────────────────────────────────────────────────────

class StoreOrdersState {
  final List<OrderDto> orders;
  final bool isLoading;
  final String? error;
  final int newOrderBadgeCount; // unread new orders

  const StoreOrdersState({
    this.orders = const [],
    this.isLoading = false,
    this.error,
    this.newOrderBadgeCount = 0,
  });

  StoreOrdersState copyWith({
    List<OrderDto>? orders,
    bool? isLoading,
    String? error,
    int? newOrderBadgeCount,
  }) {
    return StoreOrdersState(
      orders: orders ?? this.orders,
      isLoading: isLoading ?? this.isLoading,
      error: error,
      newOrderBadgeCount: newOrderBadgeCount ?? this.newOrderBadgeCount,
    );
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// StoreOrdersNotifier
// ─────────────────────────────────────────────────────────────────────────────

class StoreOrdersNotifier extends Notifier<StoreOrdersState> {
  StreamSubscription<StoreOrderCreatedEvent>? _orderCreatedSub;
  StreamSubscription<StoreOrderStatusChangedEvent>? _orderStatusSub;

  @override
  StoreOrdersState build() {
    ref.onDispose(_dispose);
    _initSignalR();

    ref.listen<StoreSignalRState>(storeSignalRServiceProvider, (previous, next) {
      if (next == StoreSignalRState.connected && previous != StoreSignalRState.connected) {
        debugPrint('[StoreOrdersNotifier] SignalR reconnected/connected, refreshing orders...');
        loadOrders();
      }
    });

    return const StoreOrdersState(isLoading: true);
  }

  // ── Init SignalR ────────────────────────────────────────────────────────────

  Future<void> _initSignalR() async {
    final signalR = ref.read(storeSignalRServiceProvider.notifier);
    final wasConnected = ref.read(storeSignalRServiceProvider) == StoreSignalRState.connected;

    try {
      await signalR.connect();
      _subscribeToEvents();
    } catch (e) {
      debugPrint('[StoreOrdersNotifier] SignalR connect error: $e');
    }

    final isConnectedNow = ref.read(storeSignalRServiceProvider) == StoreSignalRState.connected;
    if (wasConnected || !isConnectedNow) {
      await loadOrders();
    }
  }

  void _subscribeToEvents() {
    final signalR = ref.read(storeSignalRServiceProvider.notifier);

    _orderCreatedSub?.cancel();
    _orderCreatedSub = signalR.onOrderCreated.listen((event) {
      debugPrint('[StoreOrdersNotifier] 🔔 New order via SignalR: ${event.order.id}');
      // Prepend new order and increment badge
      final updatedOrders = [event.order, ...state.orders];
      state = state.copyWith(
        orders: updatedOrders,
        newOrderBadgeCount: state.newOrderBadgeCount + 1,
      );
    });

    _orderStatusSub?.cancel();
    _orderStatusSub = signalR.onOrderStatusChanged.listen((event) {
      debugPrint('[StoreOrdersNotifier] Order ${event.orderId} → ${event.status}');
      final updatedOrders = state.orders.map((o) {
        if (o.id == event.orderId) {
          // Reconstruct with updated status (OrderDto is immutable)
          return OrderDto(
            id: o.id,
            status: event.status,
            shopId: o.shopId,
            customerId: o.customerId,
            deliveryFee: o.deliveryFee,
            distanceKm: o.distanceKm,
            expectedDeliveryTime: o.expectedDeliveryTime,
            items: o.items,
            assignedRiderId: o.assignedRiderId,
            pickupLat: o.pickupLat,
            pickupLng: o.pickupLng,
            dropoffLat: o.dropoffLat,
            dropoffLng: o.dropoffLng,
            trackingCode: o.trackingCode,
            refNumber: o.refNumber,
            encodedPolyline: o.encodedPolyline,
            createdAt: o.createdAt,
            assignedAt: o.assignedAt,
            completedAt: o.completedAt,
          );
        }
        return o;
      }).toList();
      state = state.copyWith(orders: updatedOrders);
    });
  }

  // ── Load from REST ──────────────────────────────────────────────────────────

  Future<void> loadOrders() async {
    state = state.copyWith(isLoading: true, error: null);
    try {
      final orderApi = ref.read(orderApiServiceProvider);
      // GET /api/v1/orders?shopId=... — fetches orders for this shop
      final result = await orderApi.getShopOrders();
      state = state.copyWith(isLoading: false, orders: result);
    } catch (e) {
      debugPrint('[StoreOrdersNotifier] loadOrders error: $e');
      state = state.copyWith(isLoading: false, error: e.toString());
    }
  }

  // ── Mark as read ────────────────────────────────────────────────────────────

  void clearBadge() {
    if (state.newOrderBadgeCount > 0) {
      state = state.copyWith(newOrderBadgeCount: 0);
    }
  }

  // ── Accept / Reject ─────────────────────────────────────────────────────────

  Future<void> acceptOrder(String orderId) async {
    try {
      final orderApi = ref.read(orderApiServiceProvider);
      await orderApi.acceptOrderByStore(orderId);
      await loadOrders();
    } catch (e) {
      debugPrint('[StoreOrdersNotifier] acceptOrder error: $e');
    }
  }

  Future<void> rejectOrder(String orderId) async {
    try {
      final orderApi = ref.read(orderApiServiceProvider);
      await orderApi.updateOrderStatus(orderId, 'CANCELLED');
      await loadOrders();
    } catch (e) {
      debugPrint('[StoreOrdersNotifier] rejectOrder error: $e');
    }
  }

  void _dispose() {
    _orderCreatedSub?.cancel();
    _orderStatusSub?.cancel();
    _orderCreatedSub = null;
    _orderStatusSub = null;
    ref.read(storeSignalRServiceProvider.notifier).disconnect();
  }
}

final storeOrdersProvider =
    NotifierProvider<StoreOrdersNotifier, StoreOrdersState>(
  StoreOrdersNotifier.new,
);
