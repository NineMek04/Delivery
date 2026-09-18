import 'package:latlong2/latlong.dart';

enum SimFlowPhase {
  idle('waiting'),
  scan('scan'),
  offer('offer'),
  assigned('assigned'),
  pickup('pickup'),
  delivery('delivery'),
  completed('completed');

  const SimFlowPhase(this.label);
  final String label;
}

class SimTimelineItem {
  const SimTimelineItem({
    required this.title,
    required this.detail,
    required this.time,
  });

  final String title;
  final String detail;
  final String time;
}

class ResolvedRoute {
  const ResolvedRoute({
    required this.points,
    this.fallbackReason,
    this.encodedLength,
  });

  final List<LatLng> points;
  final String? fallbackReason;
  final int? encodedLength;
}
