import 'package:flutter/material.dart';
import 'package:latlong2/latlong.dart';

import '../../../models/order.dart';
import 'map_tracking_markers.dart';
import 'map_tracking_nav_panel.dart';
import 'map_tracking_types.dart';
import 'navigation_map_overlays.dart';

class MapTrackingOverlays extends StatelessWidget {
  final LatLng? riderPoint;
  final LatLng? simRiderPoint;
  final OrderDto? order;
  final SimFlowPhase simPhase;
  final LatLng? pickup;
  final LatLng? dropoff;
  final bool isNavigatingToDropoff;
  final double? displayRouteDistance;
  final double? displayRouteDuration;
  final bool soundEnabled;
  final bool isUpdating;
  final String? nextActionLabel;
  final VoidCallback onCenter;
  final VoidCallback onOverview;
  final VoidCallback onToggleSound;
  final VoidCallback? onAdvanceOrder;

  const MapTrackingOverlays({
    super.key,
    this.riderPoint,
    this.simRiderPoint,
    this.order,
    required this.simPhase,
    this.pickup,
    this.dropoff,
    required this.isNavigatingToDropoff,
    this.displayRouteDistance,
    this.displayRouteDuration,
    required this.soundEnabled,
    required this.isUpdating,
    this.nextActionLabel,
    required this.onCenter,
    required this.onOverview,
    required this.onToggleSound,
    this.onAdvanceOrder,
  });

  @override
  Widget build(BuildContext context) {
    final activeRider = riderPoint ?? simRiderPoint;
    return Stack(
      children: [
        if (activeRider != null && (order != null || simPhase != SimFlowPhase.idle))
          Positioned(
            top: 16,
            left: 16,
            right: 16,
            child: NavigationInstructionPanel(
              riderPos: activeRider,
              activeOrder: order,
              pickup: pickup,
              dropoff: dropoff,
              routeDistance: displayRouteDistance,
              simPhase: simPhase,
              forceDropoff: isNavigatingToDropoff,
            ),
          ),
        if (order != null && riderPoint != null)
          Positioned(
            right: 16,
            top: 132,
            child: NavigationFloatingControls(
              soundEnabled: soundEnabled,
              onCenter: onCenter,
              onOverview: onOverview,
              onToggleSound: onToggleSound,
              onReport: () {
                ScaffoldMessenger.of(context).showSnackBar(
                  const SnackBar(content: Text('Route issue report queued')),
                );
              },
            ),
          ),
        if (order != null && riderPoint != null)
          Positioned(
            left: 0,
            right: 0,
            bottom: 0,
            child: NavigationBottomEtaPanel(
              title: isNavigatingToDropoff ? 'To dropoff' : 'To pickup',
              etaText: formatDuration(displayRouteDuration),
              distanceText: formatDistance(displayRouteDistance),
              statusText: order!.status,
              actionLabel: nextActionLabel,
              actionBusy: isUpdating,
              onAction: onAdvanceOrder,
              onClose: () => Navigator.of(context).maybePop(),
              onOverview: onOverview,
            ),
          ),
      ],
    );
  }
}
