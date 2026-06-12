import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/api/services/order_api_service.dart';
import '../../../core/signalr/store_signalr_service.dart';
import '../../../models/order.dart';

import 'store_providers.dart';

// ─────────────────────────────────────────────────────────────────────────────
// StoreOrdersState — holds the list of incoming/active orders for the store
// ─────────────────────────────────────────────────────────────────────────────

class StoreOrdersState {
  final List<OrderDto> orders;
  final bool isLoading;
  final String? error;
  final int newOrderBadgeCount; // unread new orders
  final Set<String> processingOrderIds;

  const StoreOrdersState({
    this.orders = const [],
    this.isLoading = false,
    this.error,
    this.newOrderBadgeCount = 0,
    this.processingOrderIds = const {},
  });

  StoreOrdersState copyWith({
    List<OrderDto>? orders,
    bool? isLoading,
    String? error,
    int? newOrderBadgeCount,
    Set<String>? processingOrderIds,
  }) {
    return StoreOrdersState(
      orders: orders ?? this.orders,
      isLoading: isLoading ?? this.isLoading,
      error: error,
      newOrderBadgeCount: newOrderBadgeCount ?? this.newOrderBadgeCount,
      processingOrderIds: processingOrderIds ?? this.processingOrderIds,
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
    _orderCreatedSub = signalR.onOrderCreated.listen((event) async {
      debugPrint('[StoreOrdersNotifier] 🔔 New order via SignalR: ${event.order.id}');
      try {
        final shop = await ref.read(currentShopProvider.future);
        if (shop == null || event.order.shopId != shop.id) {
          debugPrint('[StoreOrdersNotifier] Ignoring order for different shop: ${event.order.shopId} (my shop: ${shop?.id})');
          return;
        }

        // Avoid adding duplicate orders
        final exists = state.orders.any((o) => o.id == event.order.id);
        if (exists) return;

        // Prepend new order and increment badge
        final updatedOrders = [event.order, ...state.orders];
        state = state.copyWith(
          orders: updatedOrders,
          newOrderBadgeCount: state.newOrderBadgeCount + 1,
        );
      } catch (e) {
        debugPrint('[StoreOrdersNotifier] Error processing SignalR onOrderCreated: $e');
      }
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
            routeDistanceMeters: o.routeDistanceMeters,
            routeDurationSeconds: o.routeDurationSeconds,
            batchGroupId: o.batchGroupId,
            batchSequence: o.batchSequence,
            batchSize: o.batchSize,
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

  Future<bool> acceptOrder(String orderId) async {
    if (state.processingOrderIds.contains(orderId)) return false;
    _setProcessing(orderId, true);
    try {
      final orderApi = ref.read(orderApiServiceProvider);
      final updated = await orderApi.acceptOrderByStore(orderId);
      _replaceOrder(updated);
      return true;
    } catch (e) {
      debugPrint('[StoreOrdersNotifier] acceptOrder error: $e');
      return false;
    } finally {
      _setProcessing(orderId, false);
    }
  }

  Future<bool> rejectOrder(String orderId) async {
    if (state.processingOrderIds.contains(orderId)) return false;
    _setProcessing(orderId, true);
    try {
      final orderApi = ref.read(orderApiServiceProvider);
      final updated = await orderApi.rejectOrderByStore(orderId);
      _replaceOrder(updated);
      return true;
    } catch (e) {
      debugPrint('[StoreOrdersNotifier] rejectOrder error: $e');
      return false;
    } finally {
      _setProcessing(orderId, false);
    }
  }

  void _setProcessing(String orderId, bool isProcessing) {
    final updated = {...state.processingOrderIds};
    if (isProcessing) {
      updated.add(orderId);
    } else {
      updated.remove(orderId);
    }
    state = state.copyWith(processingOrderIds: updated);
  }

  void _replaceOrder(OrderDto updated) {
    state = state.copyWith(
      error: null,
      orders: state.orders
          .map((order) => order.id == updated.id ? updated : order)
          .toList(),
    );
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
