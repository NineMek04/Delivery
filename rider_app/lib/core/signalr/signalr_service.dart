import 'dart:async';
import 'dart:math';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:logger/logger.dart';
import 'package:signalr_netcore/iretry_policy.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../../models/dispatch_offer.dart';
import '../auth/auth_service.dart';
import '../config/environment.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

/// Order status change from SignalR `OrderStatusChanged`.
class OrderStatusChangedEvent {
  final String orderId;
  final String status;

  const OrderStatusChangedEvent({
    required this.orderId,
    required this.status,
  });
}

/// Result from `OfferAcceptedResult` hub callback.
class OfferAcceptedResult {
  final bool success;
  final String? message;

  const OfferAcceptedResult({required this.success, this.message});
}

/// Result from `RiderStatusUpdatedResult` hub callback.
class RiderStatusResult {
  final bool success;
  final String? status;
  final String? message;

  const RiderStatusResult({
    required this.success,
    this.status,
    this.message,
  });
}

/// Rider location event used by simulation mirror UI.
class RiderLocationUpdateEvent {
  final String riderId;
  final double latitude;
  final double longitude;
  final String status;
  final DateTime timestamp;

  const RiderLocationUpdateEvent({
    required this.riderId,
    required this.latitude,
    required this.longitude,
    required this.status,
    required this.timestamp,
  });
}

/// Dispatch scan start event used by simulation mirror UI.
class DispatchScanStartedEvent {
  final String orderId;
  final double? pickupLat;
  final double? pickupLng;
  final double? dropoffLat;
  final double? dropoffLng;
  final int nearbyCount;

  const DispatchScanStartedEvent({
    required this.orderId,
    required this.pickupLat,
    required this.pickupLng,
    required this.dropoffLat,
    required this.dropoffLng,
    required this.nearbyCount,
  });
}

/// Custom retry policy that implements SignalR's `IRetryPolicy` with a
/// randomized exponential backoff strategy (1s - 5s delay with jitter).
class JitteredRetryPolicy implements IRetryPolicy {
  @override
  int? nextRetryDelayInMilliseconds(RetryContext retryContext) {
    // Under the hood, SignalR client automatically resets the RetryContext state (including previousRetryCount)
    // to 0 when it successfully reconnects.
    final count = retryContext.previousRetryCount;
    
    // Exponential backoff base: 1000ms * 1.5^count
    final double baseDelay = 1000 * pow(1.5, count).toDouble();
    
    // Jitter: +/- 500ms
    final random = Random();
    final int jitter = random.nextInt(1001) - 500; // range: -500 to +500 ms
    
    int nextDelay = (baseDelay + jitter).round();
    
    // Bound the delay between 1,000ms and 5,000ms (Traffic smoothing guard)
    if (nextDelay < 1000) nextDelay = 1000;
    if (nextDelay > 5000) nextDelay = 5000;
    
    return nextDelay;
  }
}

/// SignalR client for TrackingHub (`/hubs/tracking`).
class SignalRService extends Notifier<SignalRConnectionState> {
  HubConnection? _hubConnection;

  final _offerController = StreamController<DispatchOffer>.broadcast();
  final _orderStatusController =
      StreamController<OrderStatusChangedEvent>.broadcast();
  final _offerAcceptedController =
      StreamController<OfferAcceptedResult>.broadcast();
  final _riderStatusResultController =
      StreamController<RiderStatusResult>.broadcast();
  final _riderLocationController =
      StreamController<RiderLocationUpdateEvent>.broadcast();
  final _dispatchScanStartedController =
      StreamController<DispatchScanStartedEvent>.broadcast();
  final _dispatchCandidatesRankedController =
      StreamController<int>.broadcast();
  final _dispatchOfferSentController = StreamController<DispatchOffer>.broadcast();

  @override
  SignalRConnectionState build() {
    ref.onDispose(() {
      _hubConnection?.stop();
      _offerController.close();
      _orderStatusController.close();
      _offerAcceptedController.close();
      _riderStatusResultController.close();
      _riderLocationController.close();
      _dispatchScanStartedController.close();
      _dispatchCandidatesRankedController.close();
      _dispatchOfferSentController.close();
    });
    return SignalRConnectionState.disconnected;
  }

  Stream<DispatchOffer> get onOfferReceived => _offerController.stream;

  Stream<OrderStatusChangedEvent> get onOrderStatusChanged =>
      _orderStatusController.stream;

  Stream<OfferAcceptedResult> get onOfferAcceptedResult =>
      _offerAcceptedController.stream;

  Stream<RiderStatusResult> get onRiderStatusResult =>
      _riderStatusResultController.stream;

  Stream<RiderLocationUpdateEvent> get onRiderLocationUpdated =>
      _riderLocationController.stream;

  Stream<DispatchScanStartedEvent> get onDispatchScanStarted =>
      _dispatchScanStartedController.stream;

  Stream<int> get onDispatchCandidatesRanked =>
      _dispatchCandidatesRankedController.stream;

  Stream<DispatchOffer> get onDispatchOfferSent =>
      _dispatchOfferSentController.stream;

  Future<void> connect() async {
    if (state == SignalRConnectionState.connected ||
        state == SignalRConnectionState.connecting) {
      return;
    }

    await disconnect();

    final authService = ref.read(authServiceProvider.notifier);

    _hubConnection = HubConnectionBuilder()
        .withUrl(
          Environment.signalRUrl,
          options: HttpConnectionOptions(
            accessTokenFactory: () async =>
                authService.currentToken ?? '',
          ),
        )
        .withAutomaticReconnect(reconnectPolicy: JitteredRetryPolicy())
        .build();

    _registerHandlers();

    _hubConnection!.onclose(({error}) {
      _logger.w('SignalR disconnected', error: error);
      state = SignalRConnectionState.disconnected;
    });

    _hubConnection!.onreconnecting(({error}) {
      _logger.i('SignalR reconnecting...', error: error);
      state = SignalRConnectionState.reconnecting;
    });

    _hubConnection!.onreconnected(({connectionId}) {
      _logger.i('SignalR reconnected: $connectionId');
      state = SignalRConnectionState.connected;
    });

    try {
      state = SignalRConnectionState.connecting;
      await _hubConnection!.start();
      state = SignalRConnectionState.connected;
      _logger.i('SignalR connected to ${Environment.signalRUrl}');
    } catch (e) {
      state = SignalRConnectionState.error;
      _logger.e('SignalR connection failed', error: e);
      rethrow;
    }
  }

  Future<void> disconnect() async {
    await _hubConnection?.stop();
    _hubConnection = null;
    state = SignalRConnectionState.disconnected;
  }



  /// Hub: UpdateStatus(status).
  Future<bool> updateStatus(String status) async {
    if (state != SignalRConnectionState.connected) return false;

    try {
      final result = await _hubConnection!.invoke('UpdateStatus', args: [status]);
      return result == true;
    } catch (e) {
      _logger.e('Failed to update rider status', error: e);
      return false;
    }
  }

  /// Hub: UpdateLocation(lat, lng, accuracy).
  Future<void> updateLocation(double lat, double lng, double accuracy) async {
    await sendLocationUpdate(lat: lat, lng: lng, accuracy: accuracy);
  }

  Future<void> sendLocationUpdate({
    required double lat,
    required double lng,
    required double accuracy,
  }) async {
    if (state != SignalRConnectionState.connected) return;
    try {
      await _hubConnection!.invoke(
        'UpdateLocation',
        args: [lat, lng, accuracy],
      );
    } catch (e) {
      _logger.e('Failed to update location', error: e);
    }
  }

  /// Hub: AcceptOffer(offerId, version)
  Future<void> acceptOffer({
    required String offerId,
    required int version,
  }) async {
    if (state != SignalRConnectionState.connected) return;
    await _hubConnection!.invoke('AcceptOffer', args: [offerId, version]);
  }

  /// Hub: RejectOffer(offerId, orderId)
  Future<void> rejectOffer({
    required String offerId,
    required String orderId,
  }) async {
    if (state != SignalRConnectionState.connected) return;
    await _hubConnection!.invoke('RejectOffer', args: [offerId, orderId]);
  }

  /// Hub: UpdateHeartbeat — keep presence alive + state sync after reconnect.
  Future<void> sendHeartbeat() async {
    if (state != SignalRConnectionState.connected) return;
    try {
      await _hubConnection!.invoke('UpdateHeartbeat');
    } catch (e) {
      _logger.w('Heartbeat failed', error: e);
    }
  }

  void _registerHandlers() {
    final hub = _hubConnection!;

    hub.on('OfferReceived', (args) {
      if (args == null || args.isEmpty) return;
      try {
        final offer = DispatchOffer.fromJson(_asJsonMap(args.first));
        _offerController.add(offer);
        _logger.i('Offer received: ${offer.offerId}');
      } catch (e) {
        _logger.e('Failed to parse OfferReceived', error: e);
      }
    });

    hub.on('OrderStatusChanged', (args) {
      if (args == null || args.isEmpty) return;
      var orderId = '';
      var status = '';
      if (args.length >= 2) {
        orderId = args[0]?.toString() ?? '';
        status = args[1]?.toString() ?? '';
      } else {
        final map = _maybeAsJsonMap(args.first);
        if (map != null) {
          orderId = map['orderId']?.toString() ?? map['OrderId']?.toString() ?? '';
          status = map['newStatus']?.toString() ??
              map['NewStatus']?.toString() ??
              map['status']?.toString() ??
              map['Status']?.toString() ??
              '';
        }
      }
      if (orderId.isEmpty || status.isEmpty) return;
      _orderStatusController.add(
        OrderStatusChangedEvent(orderId: orderId, status: status),
      );
    });

    hub.on('OfferAcceptedResult', (args) {
      if (args == null || args.isEmpty) return;
      try {
        final map = _asJsonMap(args.first);
        _offerAcceptedController.add(
          OfferAcceptedResult(
            success: map['Success'] == true || map['success'] == true,
            message: map['Message']?.toString() ?? map['message']?.toString(),
          ),
        );
      } catch (e) {
        _logger.e('Failed to parse OfferAcceptedResult', error: e);
      }
    });

    hub.on('RiderStatusUpdatedResult', (args) {
      if (args == null || args.isEmpty) return;
      try {
        final map = _asJsonMap(args.first);
        _riderStatusResultController.add(
          RiderStatusResult(
            success: map['Success'] == true || map['success'] == true,
            status: map['Status']?.toString() ?? map['status']?.toString(),
            message: map['Message']?.toString() ?? map['message']?.toString(),
          ),
        );
      } catch (e) {
        _logger.e('Failed to parse RiderStatusUpdatedResult', error: e);
      }
    });

    hub.on('RiderLocationUpdated', (args) {
      if (args == null || args.isEmpty) return;
      final map = _maybeAsJsonMap(args.first);
      if (map == null) return;
      final riderId = map['riderId']?.toString() ?? map['RiderId']?.toString() ?? '';
      final latitude = _toDouble(
        map['latitude'] ?? map['Latitude'] ?? map['lat'] ?? map['Lat'],
      );
      final longitude = _toDouble(
        map['longitude'] ?? map['Longitude'] ?? map['lng'] ?? map['Lng'],
      );
      if (riderId.isEmpty || latitude == null || longitude == null) return;

      final timestampRaw = map['timestamp']?.toString() ?? map['Timestamp']?.toString();
      _riderLocationController.add(
        RiderLocationUpdateEvent(
          riderId: riderId,
          latitude: latitude,
          longitude: longitude,
          status: map['status']?.toString() ?? map['Status']?.toString() ?? 'UNKNOWN',
          timestamp: timestampRaw != null ? (DateTime.tryParse(timestampRaw) ?? DateTime.now()) : DateTime.now(),
        ),
      );
    });

    hub.on('DispatchScanStarted', (args) {
      if (args == null || args.isEmpty) return;
      final map = _maybeAsJsonMap(args.first);
      if (map == null) return;

      final order = _maybeAsJsonMap(map['order']) ?? _maybeAsJsonMap(map['Order']);
      final orderId = order?['id']?.toString() ?? order?['Id']?.toString() ?? '';
      final pickupLat = _toDouble(
        map['pickupLat'] ?? map['PickupLat'] ?? order?['pickupLat'] ?? order?['PickupLat'],
      );
      final pickupLng = _toDouble(
        map['pickupLng'] ?? map['PickupLng'] ?? order?['pickupLng'] ?? order?['PickupLng'],
      );
      final dropoffLat = _toDouble(
        order?['dropoffLat'] ?? order?['DropoffLat'],
      );
      final dropoffLng = _toDouble(
        order?['dropoffLng'] ?? order?['DropoffLng'],
      );
      final nearby = map['nearbyRiders'] ?? map['NearbyRiders'];
      final nearbyCount = nearby is List ? nearby.length : 0;

      _dispatchScanStartedController.add(
        DispatchScanStartedEvent(
          orderId: orderId,
          pickupLat: pickupLat,
          pickupLng: pickupLng,
          dropoffLat: dropoffLat,
          dropoffLng: dropoffLng,
          nearbyCount: nearbyCount,
        ),
      );
    });

    hub.on('DispatchCandidatesRanked', (args) {
      if (args == null || args.isEmpty) return;
      final map = _maybeAsJsonMap(args.first);
      if (map == null) return;
      final candidates = map['rankedCandidates'] ?? map['RankedCandidates'];
      final count = candidates is List ? candidates.length : 0;
      _dispatchCandidatesRankedController.add(count);
    });

    hub.on('DispatchOfferSent', (args) {
      if (args == null || args.isEmpty) return;
      try {
        final map = _asJsonMap(args.first);
        final offer = DispatchOffer.fromJson(map);
        _dispatchOfferSentController.add(offer);
      } catch (_) {
        // Ignore payload shape mismatch for non-rider audiences.
      }
    });
  }

  Map<String, dynamic> _asJsonMap(Object? value) {
    if (value is Map<String, dynamic>) return value;
    if (value is Map) return Map<String, dynamic>.from(value);
    throw FormatException('Expected map payload, got $value');
  }

  Map<String, dynamic>? _maybeAsJsonMap(Object? value) {
    if (value is Map<String, dynamic>) return value;
    if (value is Map) return Map<String, dynamic>.from(value);
    return null;
  }

  double? _toDouble(dynamic value) {
    if (value == null) return null;
    if (value is num) return value.toDouble();
    return double.tryParse(value.toString());
  }
}

enum SignalRConnectionState {
  disconnected,
  connecting,
  connected,
  reconnecting,
  error,
}

final signalRServiceProvider =
    NotifierProvider<SignalRService, SignalRConnectionState>(
  SignalRService.new,
);
