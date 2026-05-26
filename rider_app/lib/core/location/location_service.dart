import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';
import 'package:logger/logger.dart';
import '../config/environment.dart';
import '../signalr/signalr_service.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

/// Location Service — GPS tracking สำหรับ Rider.
///
/// นี่คือหัวใจของ Rider App:
/// 1. ดึงตำแหน่ง GPS ของ Rider แบบ real-time
/// 2. ส่งพิกัดผ่าน SignalR → .NET Backend → PostgreSQL/PostGIS
/// 3. Backend broadcast ไปยัง Angular Dashboard (admin-dashboard)
///
/// Data Flow (จาก AI-BLUEPRINT.md):
/// ```
/// Flutter App ──(SignalR)──► .NET Backend ──► PostgreSQL/PostGIS
///                                │
///                          Angular Dashboard
///                          (Real-time Map)
/// ```
class LocationService extends Notifier<LocationState> {
  StreamSubscription<Position>? _positionSubscription;
  final List<Position> _locationHistory = [];

  @override
  LocationState build() {
    ref.onDispose(() {
      _positionSubscription?.cancel();
    });
    return const LocationState();
  }

  /// ตรวจสอบ permissions และเริ่ม GPS tracking.
  Future<bool> startTracking() async {
    // ── 1. ตรวจสอบ Location Service เปิดอยู่ ──────────────────────
    final serviceEnabled = await Geolocator.isLocationServiceEnabled();
    if (!serviceEnabled) {
      _logger.w('📍 Location services are disabled');
      state = state.copyWith(
        error: 'Location services are disabled. Please enable GPS.',
      );
      return false;
    }

    // ── 2. ตรวจสอบ Permissions ────────────────────────────────────
    var permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied) {
      permission = await Geolocator.requestPermission();
      if (permission == LocationPermission.denied) {
        _logger.w('📍 Location permission denied');
        state = state.copyWith(error: 'Location permission denied.');
        return false;
      }
    }

    if (permission == LocationPermission.deniedForever) {
      _logger.w('📍 Location permission permanently denied');
      state = state.copyWith(
        error: 'Location permission permanently denied. Please enable in Settings.',
      );
      return false;
    }

    // ── 3. ดึงตำแหน่งเริ่มต้น ─────────────────────────────────────
    try {
      final position = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.high,
        ),
      );

      if (position.accuracy <= 50.0) {
        state = LocationState(
          latitude: position.latitude,
          longitude: position.longitude,
          accuracy: position.accuracy,
          heading: _normalizeHeading(position.heading),
          isTracking: true,
          lastUpdated: DateTime.now(),
        );
        _logger.i(
          'Initial GPS accepted: ${position.latitude}, ${position.longitude} (${position.accuracy}m)',
        );
      } else {
        // Tracking is active, but we intentionally wait for a usable point.
        state = const LocationState(isTracking: true);
        _logger.d(
          'Initial GPS filtered: accuracy ${position.accuracy}m is > 50m',
        );
      }
    } catch (e) {
      _logger.e('❌ Failed to get initial position', error: e);
    }

    // ── 4. เริ่ม stream ตำแหน่ง ─────────────────────────────────────
    LocationSettings locationSettings;

    if (defaultTargetPlatform == TargetPlatform.android) {
      locationSettings = AndroidSettings(
        accuracy: LocationAccuracy.high,
        distanceFilter: Environment.gpsDistanceFilter,
        forceLocationManager: true,
        intervalDuration: const Duration(seconds: 10),
        foregroundNotificationConfig: const ForegroundNotificationConfig(
          notificationText: "แอปกำลังติดตามตำแหน่งของคุณเบื้องหลัง (สามารถกดยกเลิกการติดตามได้ในแอป)",
          notificationTitle: "Rider App เปิดใช้งาน GPS",
          enableWakeLock: true,
        ),
      );
    } else if (defaultTargetPlatform == TargetPlatform.iOS) {
      locationSettings = AppleSettings(
        accuracy: LocationAccuracy.high,
        activityType: ActivityType.automotiveNavigation,
        distanceFilter: Environment.gpsDistanceFilter,
        pauseLocationUpdatesAutomatically: true,
        showBackgroundLocationIndicator: true,
        allowBackgroundLocationUpdates: true,
      );
    } else {
      locationSettings = LocationSettings(
        accuracy: LocationAccuracy.high,
        distanceFilter: Environment.gpsDistanceFilter,
      );
    }

    _positionSubscription = Geolocator.getPositionStream(
      locationSettings: locationSettings,
    ).listen(
      _onPositionUpdate,
      onError: (error) {
        _logger.e('❌ GPS stream error', error: error);
        state = state.copyWith(error: 'GPS tracking error: $error');
      },
    );

    _logger.i('🛰️ GPS tracking started (filter: ${Environment.gpsDistanceFilter}m)');
    return true;
  }

  /// หยุด GPS tracking.
  Future<void> stopTracking() async {
    await _positionSubscription?.cancel();
    _positionSubscription = null;
    state = state.copyWith(isTracking: false);
    _logger.i('🛑 GPS tracking stopped');
  }

  /// Handler สำหรับ position update.
  void _onPositionUpdate(Position position) {
    // ── 5. ตัวกรองพิกัด (Noise Filtering) ────────────────────────
    if (position.accuracy > 50.0) {
      _logger.d('🛑 GPS Noise filtered: accuracy ${position.accuracy}m is > 50m');
      return;
    }

    // กรองด้วย Simple Moving Average (SMA) จาก 3 พิกัดล่าสุดเพื่อลด Jitter
    _locationHistory.add(position);
    if (_locationHistory.length > 3) {
      _locationHistory.removeAt(0);
    }

    double sumLat = 0;
    double sumLng = 0;
    double sumAccuracy = 0;
    for (var pos in _locationHistory) {
      sumLat += pos.latitude;
      sumLng += pos.longitude;
      sumAccuracy += pos.accuracy;
    }
    final avgLat = sumLat / _locationHistory.length;
    final avgLng = sumLng / _locationHistory.length;
    final avgAccuracy = sumAccuracy / _locationHistory.length;

    state = state.copyWith(
      latitude: avgLat,
      longitude: avgLng,
      accuracy: avgAccuracy,
      heading: _normalizeHeading(position.heading),
      lastUpdated: DateTime.now(),
      error: null,
    );

    // ส่งพิกัดไปยัง Backend ผ่าน SignalR
    final signalRService = ref.read(signalRServiceProvider.notifier);
    signalRService.sendLocationUpdate(
      lat: avgLat,
      lng: avgLng,
      accuracy: avgAccuracy,
    );
  }

  double? _normalizeHeading(double heading) {
    if (!heading.isFinite || heading < 0) return null;
    return heading % 360;
  }
}

/// สถานะ GPS location ของ Rider.
class LocationState {
  final double? latitude;
  final double? longitude;
  final double? accuracy;
  final double? heading;
  final bool isTracking;
  final DateTime? lastUpdated;
  final String? error;

  const LocationState({
    this.latitude,
    this.longitude,
    this.accuracy,
    this.heading,
    this.isTracking = false,
    this.lastUpdated,
    this.error,
  });

  LocationState copyWith({
    double? latitude,
    double? longitude,
    double? accuracy,
    double? heading,
    bool? isTracking,
    DateTime? lastUpdated,
    String? error,
  }) {
    return LocationState(
      latitude: latitude ?? this.latitude,
      longitude: longitude ?? this.longitude,
      accuracy: accuracy ?? this.accuracy,
      heading: heading ?? this.heading,
      isTracking: isTracking ?? this.isTracking,
      lastUpdated: lastUpdated ?? this.lastUpdated,
      error: error,
    );
  }
}

final locationServiceProvider = NotifierProvider<LocationService, LocationState>(
  LocationService.new,
);
