import 'dart:async';
import 'dart:math' as math;
import 'package:flutter/material.dart';
import 'package:latlong2/latlong.dart';

import '../../../core/signalr/signalr_service.dart';
import '../../../models/dispatch_offer.dart';
import '../../../shared/utils/polyline_util.dart';
import 'map_tracking_types.dart';

class MapTrackingSimHandler {
  static const int maxTimelineItems = 8;

  bool simMirrorEnabled = false;
  SimFlowPhase simPhase = SimFlowPhase.idle;
  String simOrderId = 'WAITING';
  int simCandidateCount = 0;
  String simRiderLabel = 'NONE';
  LatLng? simRiderPoint;
  LatLng? simPickupPoint;
  LatLng? simDropoffPoint;
  List<LatLng> simPickupRoute = const [];
  List<LatLng> simDeliveryRoute = const [];
  final List<SimTimelineItem> timeline = [];

  StreamSubscription<DispatchScanStartedEvent>? _scanSub;
  StreamSubscription<int>? _rankSub;
  StreamSubscription<DispatchOffer>? _offerSub;
  StreamSubscription<OrderStatusChangedEvent>? _statusSub;
  StreamSubscription<RiderLocationUpdateEvent>? _riderLocationSub;

  void bindStreams({
    required SignalRService signalRService,
    required String? currentRiderId,
    required String? assignedRiderId,
    required void Function(double lat, double lng) onCurrentRiderLocation,
    required void Function(LatLng? target) onCenter,
    required VoidCallback onNotify,
  }) {
    _scanSub = signalRService.onDispatchScanStarted.listen((event) {
      if (!simMirrorEnabled) return;
      simPhase = SimFlowPhase.scan;
      simOrderId = _shortOrder(event.orderId);
      simCandidateCount = event.nearbyCount;
      simPickupPoint = _latLngOrNull(event.pickupLat, event.pickupLng);
      simDropoffPoint = _latLngOrNull(event.dropoffLat, event.dropoffLng);
      _pushTimeline('AI scan started', 'Found ${event.nearbyCount} nearby riders');
      onNotify();
      onCenter(simPickupPoint ?? simDropoffPoint);
    });

    _rankSub = signalRService.onDispatchCandidatesRanked.listen((count) {
      if (!simMirrorEnabled) return;
      simPhase = SimFlowPhase.offer;
      simCandidateCount = count;
      _pushTimeline('AI ranking completed', 'Ranked $count rider candidates');
      onNotify();
    });

    _offerSub = signalRService.onDispatchOfferSent.listen((offer) {
      if (!simMirrorEnabled) return;
      final riderId = offer.riderId ?? offer.order.id;
      simPhase = SimFlowPhase.offer;
      simOrderId = _shortOrder(offer.order.id);
      simRiderLabel = _shortRider(riderId);
      simPickupPoint = _latLngOrNull(offer.order.pickupLat, offer.order.pickupLng);
      simDropoffPoint = _latLngOrNull(offer.order.dropoffLat, offer.order.dropoffLng);
      simPickupRoute = offer.pickupRoute?.encodedPolyline?.isNotEmpty == true
          ? decodePolyline(offer.pickupRoute!.encodedPolyline!)
          : const [];
      simDeliveryRoute = offer.order.encodedPolyline?.isNotEmpty == true
          ? decodePolyline(offer.order.encodedPolyline!)
          : const [];
      _pushTimeline('Offer sent', '$simRiderLabel received order offer');
      onNotify();
    });

    _statusSub = signalRService.onOrderStatusChanged.listen((event) {
      if (!simMirrorEnabled) return;
      final currentShortOrderId = _shortOrder(event.orderId);
      if (simOrderId != currentShortOrderId) return;

      final nextPhase = _phaseFromStatus(event.status);
      if (nextPhase == null) return;
      simPhase = nextPhase;
      simOrderId = _shortOrder(event.orderId);
      _pushTimeline('Order status', '${_shortOrder(event.orderId)} -> ${event.status}');
      onNotify();
    });

    _riderLocationSub = signalRService.onRiderLocationUpdated.listen((event) {
      if (event.riderId == currentRiderId) {
        onCurrentRiderLocation(event.latitude, event.longitude);
      }

      if (assignedRiderId != null && event.riderId != assignedRiderId) {
        return;
      }

      if (simMirrorEnabled || assignedRiderId != null) {
        simRiderPoint = LatLng(event.latitude, event.longitude);
        simRiderLabel = _shortRider(event.riderId);
        onNotify();
      }
    });
  }

  void toggleSimMirror(VoidCallback onNotify) {
    simMirrorEnabled = !simMirrorEnabled;
    if (!simMirrorEnabled) {
      simPhase = SimFlowPhase.idle;
      simOrderId = 'WAITING';
      simCandidateCount = 0;
      simRiderLabel = 'NONE';
      simRiderPoint = null;
      simPickupPoint = null;
      simDropoffPoint = null;
      simPickupRoute = const [];
      simDeliveryRoute = const [];
      timeline.clear();
    } else {
      _pushTimeline('Sim mirror enabled', 'Listening to dashboard simulation events');
    }
    onNotify();
  }

  void dispose() {
    _scanSub?.cancel();
    _rankSub?.cancel();
    _offerSub?.cancel();
    _statusSub?.cancel();
    _riderLocationSub?.cancel();
  }

  void _pushTimeline(String title, String detail) {
    final now = TimeOfDay.now();
    final timeStr = '${now.hour.toString().padLeft(2, '0')}:${now.minute.toString().padLeft(2, '0')}';
    timeline.insert(
      0,
      SimTimelineItem(title: title, detail: detail, time: timeStr),
    );
    if (timeline.length > maxTimelineItems) {
      timeline.removeRange(maxTimelineItems, timeline.length);
    }
  }

  SimFlowPhase? _phaseFromStatus(String statusRaw) {
    final status = statusRaw.toUpperCase();
    if (status == 'ASSIGNED') return SimFlowPhase.assigned;
    if (status == 'PICKING_UP') return SimFlowPhase.pickup;
    if (status == 'DELIVERING') return SimFlowPhase.delivery;
    if (status == 'COMPLETED') return SimFlowPhase.completed;
    return null;
  }

  String _shortOrder(String orderId) {
    if (orderId.isEmpty) return 'WAITING';
    return 'ORD-${orderId.substring(0, math.min(6, orderId.length)).toUpperCase()}';
  }

  String _shortRider(String riderId) {
    if (riderId.isEmpty) return 'NONE';
    return 'RID-${riderId.substring(0, math.min(6, riderId.length)).toUpperCase()}';
  }

  LatLng? _latLngOrNull(double? lat, double? lng) {
    if (lat == null || lng == null) return null;
    return LatLng(lat, lng);
  }
}
