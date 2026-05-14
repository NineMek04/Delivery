import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../../../models/route_result.dart';

part 'tracking_provider.g.dart';

/// Tracking Provider — state สำหรับ GPS tracking + route display.
///
/// เชื่อมต่อกับ:
/// - LocationService (GPS streaming)
/// - SignalRService (ส่ง/รับ location updates)
/// - AI Service (VRP route results)
///
/// TODO: Implement tracking logic
@riverpod
class TrackingNotifier extends _$TrackingNotifier {
  @override
  TrackingState build() {
    return const TrackingState();
  }

  /// เริ่ม GPS tracking + SignalR connection.
  Future<void> startTracking() async {
    state = state.copyWith(isTracking: true);

    // TODO:
    // 1. ref.read(locationServiceProvider.notifier).startTracking();
    // 2. ref.read(signalRServiceProvider.notifier).connect();
  }

  /// หยุด GPS tracking.
  Future<void> stopTracking() async {
    state = state.copyWith(isTracking: false);

    // TODO:
    // 1. ref.read(locationServiceProvider.notifier).stopTracking();
  }

  /// อัปเดตเส้นทางจาก AI Service.
  void updateRoute(RouteResult route) {
    state = state.copyWith(currentRoute: route);
  }
}

/// Tracking state.
class TrackingState {
  final bool isTracking;
  final RouteResult? currentRoute;
  final String? error;

  const TrackingState({
    this.isTracking = false,
    this.currentRoute,
    this.error,
  });

  TrackingState copyWith({
    bool? isTracking,
    RouteResult? currentRoute,
    String? error,
  }) {
    return TrackingState(
      isTracking: isTracking ?? this.isTracking,
      currentRoute: currentRoute ?? this.currentRoute,
      error: error,
    );
  }
}
