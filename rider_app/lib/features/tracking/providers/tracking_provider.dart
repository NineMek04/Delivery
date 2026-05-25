import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/location/location_service.dart';
import '../../../core/session/rider_session_service.dart';
import '../../../core/signalr/signalr_service.dart';
import '../../../models/route_result.dart';

final trackingNotifierProvider =
    NotifierProvider<TrackingNotifier, TrackingState>(
  TrackingNotifier.new,
);

/// Tracking state — GPS position + route overlay + session connectivity.
class TrackingNotifier extends Notifier<TrackingState> {
  RouteResult? _currentRoute;

  @override
  TrackingState build() {
    final location = ref.watch(locationServiceProvider);
    final session = ref.watch(riderSessionServiceProvider);
    final signalRState = ref.watch(signalRServiceProvider);

    return TrackingState(
      isTracking: location.isTracking,
      latitude: location.latitude,
      longitude: location.longitude,
      lastUpdated: location.lastUpdated,
      locationError: location.error,
      isOnline: session.isOnline,
      signalRConnected: signalRState == SignalRConnectionState.connected,
      currentRoute: _currentRoute,
    );
  }

  Future<void> startTracking() async {
    await ref.read(riderSessionServiceProvider.notifier).goOnline();
  }

  Future<void> stopTracking() async {
    await ref.read(riderSessionServiceProvider.notifier).goOffline();
  }

  void updateRoute(RouteResult route) {
    _currentRoute = route;
    ref.invalidateSelf();
  }
}

class TrackingState {
  final bool isTracking;
  final double? latitude;
  final double? longitude;
  final DateTime? lastUpdated;
  final String? locationError;
  final bool isOnline;
  final bool signalRConnected;
  final RouteResult? currentRoute;
  final String? error;

  const TrackingState({
    this.isTracking = false,
    this.latitude,
    this.longitude,
    this.lastUpdated,
    this.locationError,
    this.isOnline = false,
    this.signalRConnected = false,
    this.currentRoute,
    this.error,
  });

  TrackingState copyWith({
    bool? isTracking,
    double? latitude,
    double? longitude,
    DateTime? lastUpdated,
    String? locationError,
    bool? isOnline,
    bool? signalRConnected,
    RouteResult? currentRoute,
    String? error,
  }) {
    return TrackingState(
      isTracking: isTracking ?? this.isTracking,
      latitude: latitude ?? this.latitude,
      longitude: longitude ?? this.longitude,
      lastUpdated: lastUpdated ?? this.lastUpdated,
      locationError: locationError,
      isOnline: isOnline ?? this.isOnline,
      signalRConnected: signalRConnected ?? this.signalRConnected,
      currentRoute: currentRoute ?? this.currentRoute,
      error: error,
    );
  }
}
