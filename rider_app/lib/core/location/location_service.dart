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

      state = LocationState(
        latitude: position.latitude,
        longitude: position.longitude,
        isTracking: true,
        lastUpdated: DateTime.now(),
      );

      _logger.i('📍 Initial position: ${position.latitude}, ${position.longitude}');
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

    state = state.copyWith(
      latitude: position.latitude,
      longitude: position.longitude,
      lastUpdated: DateTime.now(),
      error: null,
    );

    // ส่งพิกัดไปยัง Backend ผ่าน SignalR
    final signalRService = ref.read(signalRServiceProvider.notifier);
    signalRService.sendLocationUpdate(
      lat: position.latitude,
      lng: position.longitude,
    );
  }
}

/// สถานะ GPS location ของ Rider.
class LocationState {
  final double? latitude;
  final double? longitude;
  final bool isTracking;
  final DateTime? lastUpdated;
  final String? error;

  const LocationState({
    this.latitude,
    this.longitude,
    this.isTracking = false,
    this.lastUpdated,
    this.error,
  });

  LocationState copyWith({
    double? latitude,
    double? longitude,
    bool? isTracking,
    DateTime? lastUpdated,
    String? error,
  }) {
    return LocationState(
      latitude: latitude ?? this.latitude,
      longitude: longitude ?? this.longitude,
      isTracking: isTracking ?? this.isTracking,
      lastUpdated: lastUpdated ?? this.lastUpdated,
      error: error,
    );
  }
}

final locationServiceProvider = NotifierProvider<LocationService, LocationState>(
  LocationService.new,
);
