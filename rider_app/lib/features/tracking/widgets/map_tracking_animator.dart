import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import 'package:latlong2/latlong.dart';

import 'map_tracking_route_helper.dart';

class MapTrackingAnimator {
  final TickerProvider vsync;
  static const defaultCenter = LatLng(17.4138, 102.7872);

  late final ValueNotifier<LatLng> positionNotifier;
  late final ValueNotifier<double> headingNotifier;
  late final AnimationController positionAnimController;
  late final AnimationController headingAnimController;

  LatLng animatedPosition = defaultCenter;
  double animatedHeading = 0.0;
  LatLng? _animTargetPosition;
  double? _animTargetHeading;
  LatLng? _lastAnimatedPoint;
  double? _lastAnimatedHeading;
  DateTime? _lastUpdateReceivedTime;
  Duration _animationDuration = const Duration(seconds: 3);
  double? _prevLat;
  double? _prevLng;

  MapTrackingAnimator({required this.vsync}) {
    positionNotifier = ValueNotifier(animatedPosition);
    headingNotifier = ValueNotifier(animatedHeading);
    positionAnimController = AnimationController(
      vsync: vsync,
      duration: _animationDuration,
    )..addListener(_onPositionAnimTick);
    headingAnimController = AnimationController(
      vsync: vsync,
      duration: const Duration(milliseconds: 500),
    )..addListener(_onHeadingAnimTick);
  }

  void _onPositionAnimTick() {
    if (_animTargetPosition == null || _lastAnimatedPoint == null) return;
    final t = positionAnimController.value;
    final begin = _lastAnimatedPoint!;
    final end = _animTargetPosition!;
    final lat = begin.latitude + (end.latitude - begin.latitude) * t;
    final lng = begin.longitude + (end.longitude - begin.longitude) * t;
    animatedPosition = LatLng(lat, lng);
    positionNotifier.value = animatedPosition;
  }

  void _onHeadingAnimTick() {
    if (_animTargetHeading == null || _lastAnimatedHeading == null) return;
    final t = headingAnimController.value;
    final begin = _lastAnimatedHeading!;
    final end = _animTargetHeading!;
    double diff = (end - begin) % 360;
    if (diff > 180) diff -= 360;
    if (diff < -180) diff += 360;
    animatedHeading = begin + diff * t;
    headingNotifier.value = animatedHeading;
  }

  void updateRiderPoint(LatLng? activeRiderPoint, double? heading) {
    if (activeRiderPoint == null) return;
    if (activeRiderPoint.latitude == _prevLat && activeRiderPoint.longitude == _prevLng) return;

    final isFirstPoint = _prevLat == null && _prevLng == null;
    _prevLat = activeRiderPoint.latitude;
    _prevLng = activeRiderPoint.longitude;

    if (isFirstPoint) {
      animatedPosition = activeRiderPoint;
      positionNotifier.value = animatedPosition;
      if (heading != null) {
        animatedHeading = heading;
        headingNotifier.value = animatedHeading;
      }
    } else {
      _onRiderPositionChanged(activeRiderPoint, heading);
    }
  }

  void _onRiderPositionChanged(LatLng newPoint, double? heading) {
    final now = DateTime.now();
    if (_lastUpdateReceivedTime != null) {
      final diff = now.difference(_lastUpdateReceivedTime!);
      var seconds = diff.inSeconds;
      if (seconds < 1) seconds = 1;
      if (seconds > 11) seconds = 11;
      _animationDuration = Duration(seconds: seconds);
    } else {
      _animationDuration = const Duration(seconds: 3);
    }
    _lastUpdateReceivedTime = now;

    final startPoint = animatedPosition;
    _lastAnimatedPoint = startPoint;
    _animTargetPosition = newPoint;
    positionAnimController.duration = _animationDuration;
    positionAnimController.forward(from: 0.0);

    double? calculatedHeading = heading;
    if (calculatedHeading == null && startPoint != defaultCenter) {
      final dist = Geolocator.distanceBetween(
        startPoint.latitude, startPoint.longitude,
        newPoint.latitude, newPoint.longitude,
      );
      if (dist > 1.0) {
        calculatedHeading = calculateBearing(startPoint, newPoint);
      }
    }

    if (calculatedHeading != null) {
      _lastAnimatedHeading = animatedHeading;
      _animTargetHeading = calculatedHeading;
      headingAnimController.forward(from: 0.0);
    }
  }

  void dispose() {
    positionNotifier.dispose();
    headingNotifier.dispose();
    positionAnimController.dispose();
    headingAnimController.dispose();
  }
}
