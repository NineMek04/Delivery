import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../../../models/order.dart';

part 'delivery_provider.g.dart';

/// Delivery Provider — state สำหรับจัดการ orders ของ Rider.
///
/// เทียบกับ:
/// - Angular: `route.service.ts` (API calls สำหรับ route/order)
/// - .NET: `Controllers/MasterData/` (Order CRUD)
///
/// TODO: Implement order fetching/status update ผ่าน BackendApi
@riverpod
class DeliveryNotifier extends _$DeliveryNotifier {
  @override
  DeliveryState build() {
    return const DeliveryState();
  }

  /// โหลด active orders ที่ assign ให้ rider.
  Future<void> loadActiveOrders() async {
    state = state.copyWith(isLoading: true);

    try {
      // TODO: Call BackendApi
      // final response = await dio.get('/orders?assignedRiderId=...&status=ASSIGNED,DELIVERING');
      state = state.copyWith(isLoading: false);
    } catch (e) {
      state = state.copyWith(isLoading: false, error: e.toString());
    }
  }

  /// อัปเดตสถานะ order (ASSIGNED → PICKED_UP → DELIVERING → COMPLETED).
  Future<void> updateOrderStatus(String orderId, String newStatus) async {
    try {
      // TODO: Call BackendApi PUT /orders/{id}
    } catch (e) {
      state = state.copyWith(error: e.toString());
    }
  }
}

/// Delivery state.
class DeliveryState {
  final bool isLoading;
  final String? error;
  final List<OrderDto> activeOrders;
  final List<OrderDto> completedOrders;

  const DeliveryState({
    this.isLoading = false,
    this.error,
    this.activeOrders = const [],
    this.completedOrders = const [],
  });

  DeliveryState copyWith({
    bool? isLoading,
    String? error,
    List<OrderDto>? activeOrders,
    List<OrderDto>? completedOrders,
  }) {
    return DeliveryState(
      isLoading: isLoading ?? this.isLoading,
      error: error,
      activeOrders: activeOrders ?? this.activeOrders,
      completedOrders: completedOrders ?? this.completedOrders,
    );
  }
}
