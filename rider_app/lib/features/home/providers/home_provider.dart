import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/api/api_helpers.dart';
import '../../../core/api/services/order_api_service.dart';
import '../../../core/auth/auth_service.dart';
import '../../../core/session/rider_session_service.dart';
import '../../../core/signalr/signalr_service.dart';
import '../../../features/delivery/providers/delivery_provider.dart';
import '../../../models/auth_response.dart';
import '../../../models/dispatch_offer.dart';

final homeNotifierProvider = NotifierProvider<HomeNotifier, HomeState>(
  HomeNotifier.new,
);

/// Home dashboard — rider profile summary + online toggle + order counts.
class HomeNotifier extends Notifier<HomeState> {
  @override
  HomeState build() {
    ref.listen(riderSessionServiceProvider, (_, next) {
      state = state.copyWith(
        isOnline: next.isOnline,
        isTransitioning: next.isTransitioning,
        sessionError: next.error,
        incomingOffer: next.incomingOffer,
        clearOffer: next.incomingOffer == null,
        signalRConnected:
            ref.read(signalRServiceProvider) == SignalRConnectionState.connected,
      );
    });

    ref.listen(signalRServiceProvider, (_, next) {
      state = state.copyWith(
        signalRConnected: next == SignalRConnectionState.connected,
      );
    });

    final session = ref.read(riderSessionServiceProvider);
    return HomeState(
      isOnline: session.isOnline,
      isTransitioning: session.isTransitioning,
      sessionError: session.error,
      incomingOffer: session.incomingOffer,
      signalRConnected:
          ref.read(signalRServiceProvider) == SignalRConnectionState.connected,
    );
  }

  Future<void> loadDashboard() async {
    state = state.copyWith(isLoading: true, error: null);

    try {
      final userData = await ref.read(authServiceProvider.notifier).getUserData();
      UserInfo? user;
      if (userData != null) {
        user = UserInfo.fromJson(userData);
      }

      final orders = await ref.read(orderApiServiceProvider).getMyOrders();
      final activeCount = orders.where((o) {
        final s = o.status.toUpperCase();
        return s == 'ASSIGNED' || s == 'PICKING_UP' || s == 'DELIVERING';
      }).length;
      final completedCount = orders
          .where((o) => o.status.toUpperCase() == 'COMPLETED')
          .length;
      final totalKm = orders.fold<double>(
        0,
        (sum, o) => sum + o.distanceKm,
      );

      state = state.copyWith(
        isLoading: false,
        user: user,
        assignedOrderCount: activeCount,
        completedOrderCount: completedCount,
        totalDistanceKm: totalKm,
      );
    } on ApiException catch (e) {
      state = state.copyWith(isLoading: false, error: e.message);
    } catch (e) {
      state = state.copyWith(isLoading: false, error: e.toString());
    }
  }

  Future<void> setOnline(bool online) async {
    final session = ref.read(riderSessionServiceProvider.notifier);
    if (online) {
      await session.goOnline();
    } else {
      await session.goOffline();
    }
  }

  void dismissOffer() {
    ref.read(riderSessionServiceProvider.notifier).setIncomingOffer(null);
  }

  Future<void> acceptOffer() async {
    final offer = state.incomingOffer;
    if (offer == null) return;
    await ref.read(riderSessionServiceProvider.notifier).acceptOffer(offer);
    await ref.read(deliveryNotifierProvider.notifier).loadOrders();
    dismissOffer();
  }

  Future<void> rejectOffer() async {
    final offer = state.incomingOffer;
    if (offer == null) return;
    await ref.read(riderSessionServiceProvider.notifier).rejectOffer(offer);
    dismissOffer();
  }
}

class HomeState {
  final bool isLoading;
  final String? error;
  final UserInfo? user;
  final int assignedOrderCount;
  final int completedOrderCount;
  final double totalDistanceKm;
  final bool isOnline;
  final bool isTransitioning;
  final String? sessionError;
  final bool signalRConnected;
  final DispatchOffer? incomingOffer;

  const HomeState({
    this.isLoading = false,
    this.error,
    this.user,
    this.assignedOrderCount = 0,
    this.completedOrderCount = 0,
    this.totalDistanceKm = 0.0,
    this.isOnline = false,
    this.isTransitioning = false,
    this.sessionError,
    this.signalRConnected = false,
    this.incomingOffer,
  });

  HomeState copyWith({
    bool? isLoading,
    String? error,
    UserInfo? user,
    int? assignedOrderCount,
    int? completedOrderCount,
    double? totalDistanceKm,
    bool? isOnline,
    bool? isTransitioning,
    String? sessionError,
    bool? signalRConnected,
    DispatchOffer? incomingOffer,
    bool clearOffer = false,
  }) {
    return HomeState(
      isLoading: isLoading ?? this.isLoading,
      error: error,
      user: user ?? this.user,
      assignedOrderCount: assignedOrderCount ?? this.assignedOrderCount,
      completedOrderCount: completedOrderCount ?? this.completedOrderCount,
      totalDistanceKm: totalDistanceKm ?? this.totalDistanceKm,
      isOnline: isOnline ?? this.isOnline,
      isTransitioning: isTransitioning ?? this.isTransitioning,
      sessionError: sessionError,
      signalRConnected: signalRConnected ?? this.signalRConnected,
      incomingOffer: clearOffer ? null : (incomingOffer ?? this.incomingOffer),
    );
  }
}
