import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

class LatLngTween extends Tween<LatLng> {
  LatLngTween({super.begin, super.end});

  @override
  LatLng lerp(double t) {
    if (begin == null || end == null) return end ?? const LatLng(0, 0);
    final lat = begin!.latitude + (end!.latitude - begin!.latitude) * t;
    final lng = begin!.longitude + (end!.longitude - begin!.longitude) * t;
    return LatLng(lat, lng);
  }
}

class AnimatedRoutePolylineLayer extends StatelessWidget {
  final List<LatLng> points;
  final bool isPickup;
  final Animation<double> animation;

  const AnimatedRoutePolylineLayer({
    super.key,
    required this.points,
    required this.isPickup,
    required this.animation,
  });

  @override
  Widget build(BuildContext context) {
    const double dashLength = 20.0;
    const double gapLength = 12.0;
    const double totalPattern = dashLength + gapLength;
    final Color routeColor = isPickup
        ? const Color(0xFFFF9800)   // orange — pickup
        : const Color(0xFF00E5FF);  // cyan   — delivery

    return AnimatedBuilder(
      animation: animation,
      builder: (context, _) {
        final double offset = animation.value * totalPattern;
        final List<double> segments = [
          offset,
          gapLength,
          dashLength,
          gapLength,
        ];
        return PolylineLayer(
          polylines: [
            Polyline(
              points: points,
              color: routeColor.withValues(alpha: 0.90),
              strokeWidth: 5.0,
              pattern: StrokePattern.dashed(
                segments: segments,
                patternFit: PatternFit.extendFinalDash,
              ),
            ),
            Polyline(
              points: points,
              color: routeColor.withValues(alpha: 0.20),
              strokeWidth: 5.0,
            ),
          ],
        );
      },
    );
  }
}
