import 'dart:async';
import 'dart:math' as math;

import 'package:logger/logger.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../features/delivery/providers/delivery_provider.dart';
import '../../models/dispatch_offer.dart';
import '../config/app_constants.dart';
import '../database/local_database_service.dart';
import '../location/location_service.dart';
import '../signalr/signalr_service.dart';

import '../auth/auth_service.dart';
import '../auth/auth_constants.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

/// Coordinates rider online mode: SignalR + GPS + hub status.
class RiderSessionService extends Notifier<RiderSessionState> {
  StreamSubscription<DispatchOffer>? _offerSub;
  StreamSubscription<OrderStatusChangedEvent>? _orderStatusSub;
  Timer? _heartbeatTimer;
  bool _heartbeatInFlight = false;

  @override
  RiderSessionState build() {
    ref.onDispose(() {
      _stopHeartbeatTimer();
      _disposeSubscriptions();
    });

    // Listen for auth transitions to trigger restoring online state
    ref.listen<AuthStatus>(authServiceProvider, (previous, next) async {
      if (next == AuthStatus.authenticated) {
        _loadSessionState();
      } else if (next == AuthStatus.unauthenticated &&
          previous == AuthStatus.authenticated) {
        await _tearDown();
        state = const RiderSessionState();
      }
    });

    // If already authenticated during build (e.g. provider rebuilt)
    final initialAuth = ref.read(authServiceProvider);
    if (initialAuth == AuthStatus.authenticated) {
      _loadSessionState();
    }

    // ฟังเหตุการณ์ Reconnect ของ SignalR เพื่อตั้งค่าออนไลน์คนขับคืนมาอัตโนมัติ
    ref.listen<SignalRConnectionState>(signalRServiceProvider, (
      previous,
      next,
    ) async {
      if (previous == SignalRConnectionState.reconnecting &&
          next == SignalRConnectionState.connected &&
          state.isOnline) {
        final role = ref.read(authServiceProvider.notifier).userRole;
        if (role != AuthConstants.roleRider) return;

        _logger.i(
          '🔄 SignalR reconnected — restoring status to IDLE and sending heartbeat',
        );
        try {
          final signalR = ref.read(signalRServiceProvider.notifier);
          await signalR.sendHeartbeat();
          _startHeartbeatTimer();

          // Jitter: สุ่มหน่วงเวลา 500ms - 3500ms ก่อนเรียก API เพื่อกระจายโหลด (ป้องกัน Thundering Herd)
          final randomDelay = Duration(
            milliseconds: 500 + math.Random().nextInt(3000),
          );
          await Future.delayed(randomDelay);

          // Fetch latest active orders immediately on reconnection to prevent stale UI state
          await ref.read(deliveryNotifierProvider.notifier).loadOrders();
        } catch (e) {
          _logger.w('Failed to restore status after reconnect: $e');
        }
      }
    });

    ref.listen<DeliveryState>(deliveryNotifierProvider, (previous, next) {
      if (!state.isOnline) return;
      final hasActiveOrder = next.activeOrder != null;
      final hadActiveOrder = previous?.activeOrder != null;

      if (hasActiveOrder && !hadActiveOrder) {
        // Rider status changed to BUSY
        _updateGpsInterval(5); // 5s interval
      } else if (!hasActiveOrder && hadActiveOrder) {
        // Rider status changed to IDLE
        _updateGpsInterval(30); // 30s interval
      }
    });

    return const RiderSessionState();
  }

  Future<void> _loadSessionState() async {
    final role = ref.read(authServiceProvider.notifier).userRole;
    if (role != AuthConstants.roleRider) {
      _logger.d(
        '⏳ Skipping rider session restore: user is not a rider (role: $role)',
      );
      return;
    }
    try {
      final db = ref.read(localDatabaseServiceProvider);
      final isOnline = await db.getIsOnline();

      if (isOnline && !state.isOnline && !state.isTransitioning) {
        _logger.i(
          '🔌 Restoring online rider session state from local database',
        );
        Future.microtask(() async {
          try {
            await goOnline();
          } catch (e) {
            _logger.w('Failed to restore rider online session: $e');
          }
        });
      }
    } catch (e) {
      _logger.e(
        'Failed to load rider session state from local database',
        error: e,
      );
    }
  }

  /// Go online: connect SignalR, set IDLE, start GPS.
  Future<void> goOnline() async {
    final role = ref.read(authServiceProvider.notifier).userRole;
    if (role != AuthConstants.roleRider) {
      _logger.w('❌ goOnline rejected: user role is not Rider (role: $role)');
      throw Exception('Only riders can go online.');
    }
    if (state.isOnline || state.isTransitioning) return;

    state = state.copyWith(isTransitioning: true, error: null);

    try {
      _logger.d("goOnline Step 0: Listening SignalR Events");
      _listenSignalREvents();

      _logger.d("goOnline Step 1: Connecting SignalR");
      final signalR = ref.read(signalRServiceProvider.notifier);
      await signalR.connect();

      _logger.d("goOnline Step 2: Updating Status to IDLE");
      final statusUpdated = await signalR.updateStatus(
        AppConstants.statusAvailable,
      );
      if (!statusUpdated) {
        throw StateError('Backend rejected rider online status.');
      }

      _logger.d("goOnline Step 3: Sending Heartbeat");
      await signalR.sendHeartbeat();
      _startHeartbeatTimer();

      _logger.d("goOnline Step 4: Starting Location Tracking");
      final locationStarted = await ref
          .read(locationServiceProvider.notifier)
          .startTracking();
      if (!locationStarted) {
        final locState = ref.read(locationServiceProvider);
        throw Exception(locState.error ?? 'Failed to start GPS tracking');
      }

      // ส่งพิกัดปัจจุบันผ่าน SignalR ทันทีหลังเริ่ม GPS
      // เพื่อให้ Admin Dashboard แสดง marker ทันทีโดยไม่ต้องรอ batch timer
      final locState = ref.read(locationServiceProvider);
      if (locState.latitude != null && locState.longitude != null) {
        _logger.d("goOnline Step 4.5: Sending immediate GPS via SignalR");
        await signalR.updateLocation(
          locState.latitude!,
          locState.longitude!,
          locState.accuracy ?? 10.0,
        );
      }

      _logger.d("goOnline Step 6: Setting State to Online");
      state = state.copyWith(
        isOnline: true,
        isTransitioning: false,
        error: null,
      );

      _logger.d("goOnline Step 7: Saving isOnline to Database");
      // Save status to database
      await ref.read(localDatabaseServiceProvider).saveIsOnline(true);

      final hasActiveOrder = ref.read(deliveryNotifierProvider).activeOrder != null;
      _updateGpsInterval(hasActiveOrder ? 5 : 30);

      _logger.i('Rider session online');
    } catch (e, stackTrace) {
      _logger.e('Exception in goOnline: $e', error: e, stackTrace: stackTrace);
      await _tearDown();
      state = state.copyWith(
        isOnline: false,
        isTransitioning: false,
        error: e.toString(),
      );

      // Save status to database safely
      try {
        await ref.read(localDatabaseServiceProvider).saveIsOnline(false);
      } catch (dbErr, dbSt) {
        _logger.e(
          'Failed to save offline status in catch: $dbErr',
          error: dbErr,
          stackTrace: dbSt,
        );
      }
      rethrow;
    }
  }

  /// Go offline: stop GPS, set OFFLINE, disconnect SignalR.
  Future<void> goOffline() async {
    if (!state.isOnline && !state.isTransitioning) return;

    state = state.copyWith(isTransitioning: true, error: null);

    try {
      final signalR = ref.read(signalRServiceProvider.notifier);
      if (ref.read(signalRServiceProvider) ==
          SignalRConnectionState.connected) {
        final statusUpdated = await signalR.updateStatus(
          AppConstants.statusOffline,
        );
        if (!statusUpdated) {
          throw StateError(
            'Backend rejected offline status while a delivery is active.',
          );
        }
      }
      await _tearDown(notifyOffline: false);

      state = const RiderSessionState(isOnline: false, isTransitioning: false);

      // Save status to database
      await ref.read(localDatabaseServiceProvider).saveIsOnline(false);
      _logger.i('Rider session offline');
    } catch (e) {
      state = state.copyWith(isTransitioning: false, error: e.toString());
      rethrow;
    }
  }

  void setIncomingOffer(DispatchOffer? offer) {
    state = state.copyWith(
      incomingOffer: offer,
      clearOffer: offer == null,
    );
  }

  Future<void> acceptOffer(DispatchOffer offer) async {
    await ref
        .read(signalRServiceProvider.notifier)
        .acceptOffer(offerId: offer.offerId, version: offer.version);
    setIncomingOffer(null);
  }

  Future<void> rejectOffer(DispatchOffer offer) async {
    await ref
        .read(signalRServiceProvider.notifier)
        .rejectOffer(offerId: offer.offerId, orderId: offer.order.id);
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

  Future<void> _tearDown({bool notifyOffline = true}) async {
    _stopHeartbeatTimer();
    _disposeSubscriptions();
    await ref.read(locationServiceProvider.notifier).stopTracking();

    // ✅ Fix: ส่ง OFFLINE ก่อน disconnect เสมอ เพื่อให้ Backend DB อัปเดต state
    // ป้องกันปัญหา state ค้างอยู่ที่ IDLE ใน DB เมื่อ GPS fail ระหว่าง goOnline()
    // ซึ่งจะทำให้การ goOnline() ครั้งต่อไป fail ด้วย IDLE→IDLE Illegal transition
    final signalR = ref.read(signalRServiceProvider.notifier);
    if (notifyOffline &&
        ref.read(signalRServiceProvider) ==
            SignalRConnectionState.connected) {
      try {
        await signalR.updateStatus(AppConstants.statusOffline);
      } catch (e) {
        _logger.w('Could not send OFFLINE status before teardown: $e');
      }
    }
    await signalR.disconnect();
  }

  void _startHeartbeatTimer() {
    _heartbeatTimer?.cancel();
    _heartbeatTimer = Timer.periodic(
      const Duration(
        seconds: AppConstants.riderHeartbeatIntervalSeconds,
      ),
      (_) async {
        if (_heartbeatInFlight ||
            !state.isOnline ||
            ref.read(signalRServiceProvider) !=
                SignalRConnectionState.connected) {
          return;
        }

        _heartbeatInFlight = true;
        try {
          await ref.read(signalRServiceProvider.notifier).sendHeartbeat();
        } finally {
          _heartbeatInFlight = false;
        }
      },
    );
  }

  void _stopHeartbeatTimer() {
    _heartbeatTimer?.cancel();
    _heartbeatTimer = null;
    _heartbeatInFlight = false;
  }

  void _disposeSubscriptions() {
    _offerSub?.cancel();
    _orderStatusSub?.cancel();
    _offerSub = null;
    _orderStatusSub = null;
  }

  void _updateGpsInterval(int seconds) {
    try {
      final locationNotifier = ref.read(locationServiceProvider.notifier);
      final settings = locationNotifier.buildLocationSettings(intervalSeconds: seconds);
      locationNotifier.updateSettings(settings, intervalSeconds: seconds);
      _logger.i('Dynamic GPS polling rate modified to $seconds seconds');
    } catch (e) {
      _logger.w('Failed to update dynamic GPS settings: $e');
    }
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
