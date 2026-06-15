import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:dio/dio.dart';

import '../../../core/api/api_helpers.dart';
import '../../../core/api/services/order_api_service.dart';
import '../../../core/config/app_constants.dart';
import '../../../core/database/local_database_service.dart';
import '../../../models/dispatch_offer.dart';
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
          clearActiveOrder: active.isEmpty,
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
        clearActiveOrder: active.isEmpty,
        clearPickupRoute: active.isEmpty ||
            (state.pickupRouteOrderId != null &&
                state.pickupRouteOrderId != active.first.id),
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
        clearActiveOrder: active.isEmpty,
        clearPickupRoute: active.isEmpty,
      );
    } catch (e) {
      // Check if it is a network connectivity error
      bool isNetworkError = false;
      if (e is DioException) {
        final type = e.type;
        if (type == DioExceptionType.connectionTimeout ||
            type == DioExceptionType.sendTimeout ||
            type == DioExceptionType.receiveTimeout ||
            type == DioExceptionType.connectionError) {
          isNetworkError = true;
        }
      } else if (e is ApiException) {
        if (e.statusCode == null) {
          isNetworkError = true;
        }
      }

      if (isNetworkError) {
        try {
          // 1. Save to SQLite queue
          final db = ref.read(localDatabaseServiceProvider);
          await db.savePendingStatusUpdate(orderId, newStatus);

          // 2. Perform Optimistic Update
          OrderDto? existingOrder;
          try {
            existingOrder = state.activeOrders.firstWhere((o) => o.id == orderId);
          } catch (_) {
            existingOrder = state.completedOrders.firstWhere((o) => o.id == orderId);
          }

          final optimisticOrder = OrderDto(
            id: existingOrder.id,
            status: newStatus,
            pickupLat: existingOrder.pickupLat,
            pickupLng: existingOrder.pickupLng,
            dropoffLat: existingOrder.dropoffLat,
            dropoffLng: existingOrder.dropoffLng,
            expectedDeliveryTime: existingOrder.expectedDeliveryTime,
            assignedRiderId: existingOrder.assignedRiderId,
            customerId: existingOrder.customerId,
            shopId: existingOrder.shopId,
            distanceKm: existingOrder.distanceKm,
            deliveryFee: existingOrder.deliveryFee,
            trackingCode: existingOrder.trackingCode,
            refNumber: existingOrder.refNumber,
            encodedPolyline: existingOrder.encodedPolyline,
            routeDistanceMeters: existingOrder.routeDistanceMeters,
            routeDurationSeconds: existingOrder.routeDurationSeconds,
            batchGroupId: existingOrder.batchGroupId,
            batchSequence: existingOrder.batchSequence,
            batchSize: existingOrder.batchSize,
            items: existingOrder.items,
            createdAt: existingOrder.createdAt,
            assignedAt: existingOrder.assignedAt,
            completedAt: newStatus == 'COMPLETED' ? DateTime.now() : existingOrder.completedAt,
          );

          await db.saveOrder(optimisticOrder);

          final active = List<OrderDto>.from(state.activeOrders);
          final completed = List<OrderDto>.from(state.completedOrders);

          active.removeWhere((o) => o.id == orderId);
          completed.removeWhere((o) => o.id == orderId);

          if (_completedStatuses.contains(optimisticOrder.status.toUpperCase())) {
            completed.insert(0, optimisticOrder);
          } else if (_activeStatuses.contains(optimisticOrder.status.toUpperCase())) {
            active.insert(0, optimisticOrder);
          }

          state = state.copyWith(
            isUpdating: false,
            activeOrders: active,
            completedOrders: completed,
            activeOrder: active.isNotEmpty ? active.first : null,
            clearActiveOrder: active.isEmpty,
            clearPickupRoute: active.isEmpty,
            error: 'Offline: บันทึกสถานะงานแบบออฟไลน์แล้ว',
          );
          return;
        } catch (dbErr) {
          // Fall through to normal error handling if database operation fails
        }
      }

      // Normal error path
      final errorMessage = e is ApiException ? e.message : e.toString();
      state = state.copyWith(isUpdating: false, error: errorMessage);
    }
  }


  Future<void> markPickingUp(String orderId) =>
      updateOrderStatus(orderId, 'PICKING_UP');

  Future<void> markDelivering(String orderId) =>
      updateOrderStatus(orderId, AppConstants.orderDelivering);

  Future<void> markCompleted(String orderId) =>
      updateOrderStatus(orderId, AppConstants.orderCompleted);

  void rememberAcceptedOffer(DispatchOffer offer) {
    final encodedPolyline = offer.pickupRoute?.encodedPolyline;
    final provisionalOrder = OrderDto(
      id: offer.order.id,
      status: 'ASSIGNED',
      pickupLat: offer.order.pickupLat,
      pickupLng: offer.order.pickupLng,
      dropoffLat: offer.order.dropoffLat,
      dropoffLng: offer.order.dropoffLng,
      distanceKm: offer.order.distanceKm ?? 0,
      deliveryFee: offer.order.deliveryFee ?? 0,
      encodedPolyline: offer.order.encodedPolyline,
      assignedAt: DateTime.now(),
    );
    final activeOrders = [
      provisionalOrder,
      ...state.activeOrders.where((order) => order.id != provisionalOrder.id),
    ];
    state = state.copyWith(
      activeOrders: activeOrders,
      activeOrder: provisionalOrder,
      pickupRouteOrderId: offer.order.id,
      pickupEncodedPolyline: encodedPolyline,
      clearPickupRoute: encodedPolyline?.isNotEmpty != true,
    );
  }
}

class DeliveryState {
  final bool isLoading;
  final bool isUpdating;
  final String? error;
  final List<OrderDto> activeOrders;
  final List<OrderDto> completedOrders;
  final OrderDto? activeOrder;
  final String? pickupRouteOrderId;
  final String? pickupEncodedPolyline;

  const DeliveryState({
    this.isLoading = false,
    this.isUpdating = false,
    this.error,
    this.activeOrders = const [],
    this.completedOrders = const [],
    this.activeOrder,
    this.pickupRouteOrderId,
    this.pickupEncodedPolyline,
  });

  DeliveryState copyWith({
    bool? isLoading,
    bool? isUpdating,
    String? error,
    List<OrderDto>? activeOrders,
    List<OrderDto>? completedOrders,
    OrderDto? activeOrder,
    bool clearActiveOrder = false,
    String? pickupRouteOrderId,
    String? pickupEncodedPolyline,
    bool clearPickupRoute = false,
  }) {
    return DeliveryState(
      isLoading: isLoading ?? this.isLoading,
      isUpdating: isUpdating ?? this.isUpdating,
      error: error,
      activeOrders: activeOrders ?? this.activeOrders,
      completedOrders: completedOrders ?? this.completedOrders,
      activeOrder: clearActiveOrder ? null : (activeOrder ?? this.activeOrder),
      pickupRouteOrderId:
          clearPickupRoute ? null : (pickupRouteOrderId ?? this.pickupRouteOrderId),
      pickupEncodedPolyline: clearPickupRoute
          ? null
          : (pickupEncodedPolyline ?? this.pickupEncodedPolyline),
    );
  }
}
