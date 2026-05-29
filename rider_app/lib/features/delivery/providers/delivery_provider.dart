import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/api/api_helpers.dart';
import '../../../core/api/services/order_api_service.dart';
import '../../../core/config/app_constants.dart';
import '../../../core/database/local_database_service.dart';
import '../../../models/order.dart';

const _activeStatuses = {
  'OFFERING',
  'ASSIGNED',
  'PICKING_UP',
  'DELIVERING',
};

const _completedStatuses = {
  'COMPLETED',
  'CANCELLED',
};

final deliveryNotifierProvider =
    NotifierProvider<DeliveryNotifier, DeliveryState>(
  DeliveryNotifier.new,
);

/// Delivery state — active/completed orders for the logged-in rider.
class DeliveryNotifier extends Notifier<DeliveryState> {
  @override
  DeliveryState build() {
    _loadCachedOrders();
    return const DeliveryState();
  }

  Future<void> _loadCachedOrders() async {
    try {
      final db = ref.read(localDatabaseServiceProvider);
      final active = await db.getActiveOrders();
      final completed = await db.getCompletedOrders();

      if (active.isNotEmpty || completed.isNotEmpty) {
        state = state.copyWith(
          activeOrders: active,
          completedOrders: completed,
          activeOrder: active.isNotEmpty ? active.first : null,
        );
      }
    } catch (_) {}
  }

  Future<void> loadOrders() async {
    state = state.copyWith(isLoading: true, error: null);

    try {
      final orders = await ref.read(orderApiServiceProvider).getMyOrders();

      final active = orders
          .where((o) => _activeStatuses.contains(o.status.toUpperCase()))
          .toList();
      final completed = orders
          .where((o) => _completedStatuses.contains(o.status.toUpperCase()))
          .toList();

      // Save to local database
      await ref.read(localDatabaseServiceProvider).saveOrders(orders);

      state = state.copyWith(
        isLoading: false,
        activeOrders: active,
        completedOrders: completed,
        activeOrder: active.isNotEmpty ? active.first : null,
      );
    } on ApiException catch (e) {
      state = state.copyWith(isLoading: false, error: e.message);
    } catch (e) {
      state = state.copyWith(isLoading: false, error: e.toString());
    }
  }

  Future<void> updateOrderStatus(String orderId, String newStatus) async {
    state = state.copyWith(isUpdating: true, error: null);

    try {
      final updated = await ref.read(orderApiServiceProvider).updateStatus(
        orderId: orderId,
        status: newStatus,
      );

      // Save updated order to local database
      await ref.read(localDatabaseServiceProvider).saveOrder(updated);

      final active = List<OrderDto>.from(state.activeOrders);
      final completed = List<OrderDto>.from(state.completedOrders);

      active.removeWhere((o) => o.id == orderId);
      completed.removeWhere((o) => o.id == orderId);

      if (_completedStatuses.contains(updated.status.toUpperCase())) {
        completed.insert(0, updated);
      } else if (_activeStatuses.contains(updated.status.toUpperCase())) {
        active.insert(0, updated);
      }

      state = state.copyWith(
        isUpdating: false,
        activeOrders: active,
        completedOrders: completed,
        activeOrder: active.isNotEmpty ? active.first : null,
      );
    } on ApiException catch (e) {
      state = state.copyWith(isUpdating: false, error: e.message);
    } catch (e) {
      state = state.copyWith(isUpdating: false, error: e.toString());
    }
  }


  Future<void> markPickingUp(String orderId) =>
      updateOrderStatus(orderId, 'PICKING_UP');

  Future<void> markDelivering(String orderId) =>
      updateOrderStatus(orderId, AppConstants.orderDelivering);

  Future<void> markCompleted(String orderId) =>
      updateOrderStatus(orderId, AppConstants.orderCompleted);
}

class DeliveryState {
  final bool isLoading;
  final bool isUpdating;
  final String? error;
  final List<OrderDto> activeOrders;
  final List<OrderDto> completedOrders;
  final OrderDto? activeOrder;

  const DeliveryState({
    this.isLoading = false,
    this.isUpdating = false,
    this.error,
    this.activeOrders = const [],
    this.completedOrders = const [],
    this.activeOrder,
  });

  DeliveryState copyWith({
    bool? isLoading,
    bool? isUpdating,
    String? error,
    List<OrderDto>? activeOrders,
    List<OrderDto>? completedOrders,
    OrderDto? activeOrder,
    bool clearActiveOrder = false,
  }) {
    return DeliveryState(
      isLoading: isLoading ?? this.isLoading,
      isUpdating: isUpdating ?? this.isUpdating,
      error: error,
      activeOrders: activeOrders ?? this.activeOrders,
      completedOrders: completedOrders ?? this.completedOrders,
      activeOrder: clearActiveOrder ? null : (activeOrder ?? this.activeOrder),
    );
  }
}
