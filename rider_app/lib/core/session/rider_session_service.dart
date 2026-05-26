import 'dart:async';

import 'package:logger/logger.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../features/delivery/providers/delivery_provider.dart';
import '../../models/dispatch_offer.dart';
import '../config/app_constants.dart';
import '../location/location_service.dart';
import '../signalr/signalr_service.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

/// Coordinates rider online mode: SignalR + GPS + hub status.
class RiderSessionService extends Notifier<RiderSessionState> {
  StreamSubscription<DispatchOffer>? _offerSub;
  StreamSubscription<OrderStatusChangedEvent>? _orderStatusSub;

  @override
  RiderSessionState build() {
    ref.onDispose(_disposeSubscriptions);

    // ฟังเหตุการณ์ Reconnect ของ SignalR เพื่อตั้งค่าออนไลน์คนขับคืนมาอัตโนมัติ
    ref.listen<SignalRConnectionState>(signalRServiceProvider, (previous, next) async {
      if (previous == SignalRConnectionState.reconnecting &&
          next == SignalRConnectionState.connected &&
          state.isOnline) {
        _logger.i('🔄 SignalR reconnected — restoring status to IDLE and sending heartbeat');
        try {
          final signalR = ref.read(signalRServiceProvider.notifier);
          await signalR.updateStatus(AppConstants.statusAvailable);
          await signalR.sendHeartbeat();
        } catch (e) {
          _logger.w('Failed to restore status after reconnect: $e');
        }
      }
    });

    return const RiderSessionState();
  }

  /// Go online: connect SignalR, set IDLE, start GPS.
  Future<void> goOnline() async {
    if (state.isOnline) return;

    state = state.copyWith(isTransitioning: true, error: null);

    try {
      final signalR = ref.read(signalRServiceProvider.notifier);
      await signalR.connect();
      await signalR.updateStatus(AppConstants.statusAvailable);
      await signalR.sendHeartbeat();

      final locationStarted =
          await ref.read(locationServiceProvider.notifier).startTracking();
      if (!locationStarted) {
        final locState = ref.read(locationServiceProvider);
        throw Exception(locState.error ?? 'Failed to start GPS tracking');
      }

      _listenSignalREvents();

      state = state.copyWith(
        isOnline: true,
        isTransitioning: false,
        error: null,
      );
      _logger.i('Rider session online');
    } catch (e) {
      await _tearDown();
      state = state.copyWith(
        isOnline: false,
        isTransitioning: false,
        error: e.toString(),
      );
      rethrow;
    }
  }

  /// Go offline: stop GPS, set OFFLINE, disconnect SignalR.
  Future<void> goOffline() async {
    if (!state.isOnline && !state.isTransitioning) return;

    state = state.copyWith(isTransitioning: true, error: null);

    try {
      final signalR = ref.read(signalRServiceProvider.notifier);
      if (ref.read(signalRServiceProvider) == SignalRConnectionState.connected) {
        await signalR.updateStatus(AppConstants.statusOffline);
      }
      await _tearDown();

      state = const RiderSessionState(isOnline: false, isTransitioning: false);
      _logger.i('Rider session offline');
    } catch (e) {
      state = state.copyWith(isTransitioning: false, error: e.toString());
      rethrow;
    }
  }

  void setIncomingOffer(DispatchOffer? offer) {
    state = state.copyWith(incomingOffer: offer);
  }

  Future<void> acceptOffer(DispatchOffer offer) async {
    await ref.read(signalRServiceProvider.notifier).acceptOffer(
      offerId: offer.offerId,
      version: offer.version,
    );
    setIncomingOffer(null);
  }

  Future<void> rejectOffer(DispatchOffer offer) async {
    await ref.read(signalRServiceProvider.notifier).rejectOffer(
      offerId: offer.offerId,
      orderId: offer.order.id,
    );
    setIncomingOffer(null);
  }

  void _listenSignalREvents() {
    _disposeSubscriptions();

    final signalR = ref.read(signalRServiceProvider.notifier);

    _offerSub = signalR.onOfferReceived.listen((offer) {
      state = state.copyWith(incomingOffer: offer);
    });

    _orderStatusSub = signalR.onOrderStatusChanged.listen((event) {
      _logger.d('Order ${event.orderId} -> ${event.status}');
      ref.read(deliveryNotifierProvider.notifier).loadOrders();
    });
  }

  Future<void> _tearDown() async {
    _disposeSubscriptions();
    await ref.read(locationServiceProvider.notifier).stopTracking();
    await ref.read(signalRServiceProvider.notifier).disconnect();
  }

  void _disposeSubscriptions() {
    _offerSub?.cancel();
    _orderStatusSub?.cancel();
    _offerSub = null;
    _orderStatusSub = null;
  }
}

class RiderSessionState {
  final bool isOnline;
  final bool isTransitioning;
  final String? error;
  final DispatchOffer? incomingOffer;

  const RiderSessionState({
    this.isOnline = false,
    this.isTransitioning = false,
    this.error,
    this.incomingOffer,
  });

  RiderSessionState copyWith({
    bool? isOnline,
    bool? isTransitioning,
    String? error,
    DispatchOffer? incomingOffer,
    bool clearOffer = false,
  }) {
    return RiderSessionState(
      isOnline: isOnline ?? this.isOnline,
      isTransitioning: isTransitioning ?? this.isTransitioning,
      error: error,
      incomingOffer: clearOffer ? null : (incomingOffer ?? this.incomingOffer),
    );
  }
}

final riderSessionServiceProvider =
    NotifierProvider<RiderSessionService, RiderSessionState>(
  RiderSessionService.new,
);
