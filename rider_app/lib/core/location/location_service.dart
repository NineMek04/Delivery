import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';
import 'package:logger/logger.dart';
import '../config/environment.dart';
import 'gps_buffer_service.dart';
import '../auth/auth_service.dart';
import '../auth/auth_constants.dart';
import '../session/rider_session_service.dart';

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
  Timer? _mockTimer;
  int _mockIntervalSeconds = Environment.gpsUpdateIntervalSeconds;
  final List<Position> _locationHistory = [];

  @override
  LocationState build() {
    ref.onDispose(() {
      _positionSubscription?.cancel();
      _mockTimer?.cancel();
    });
    return const LocationState();
  }

  /// ตรวจสอบ permissions และเริ่ม GPS tracking.
  Future<bool> startTracking() async {
    final role = ref.read(authServiceProvider.notifier).userRole;
    if (role != AuthConstants.roleRider) {
      _logger.w('❌ startTracking rejected: user role is not Rider (role: $role)');
      return false;
    }
    ref.read(gpsBufferServiceProvider).startSyncTimer();
    if (kIsWeb) {
      _logger.i('Web platform detected. Checking GPS configuration.');
      if (Environment.enableMockGps) {
        _logger.w('ENABLE_MOCK_GPS is active. Using demo coordinates.');
        _startMockStream();
        return true;
      }

      String? locationFailure;
      try {
        // ลองดึงสิทธิ์และพิกัดจริงบน Web แบบปลอดภัยที่สุด
        final serviceEnabled = await Geolocator.isLocationServiceEnabled();
        if (serviceEnabled) {
          var permission = await Geolocator.checkPermission();
          if (permission == LocationPermission.denied) {
            permission = await Geolocator.requestPermission();
          }

          if (permission == LocationPermission.always || permission == LocationPermission.whileInUse) {
            final position = await Geolocator.getCurrentPosition(
              locationSettings: const LocationSettings(
                accuracy: LocationAccuracy.high,
              ),
            );

            // ถ้าผ่านหมดและได้ตำแหน่งมา ให้ใช้ตำแหน่งจริง
            state = LocationState(
              latitude: position.latitude,
              longitude: position.longitude,
              accuracy: position.accuracy,
              heading: _normalizeHeading(_readHeading(position)),
              isTracking: true,
              lastUpdated: DateTime.now(),
            );

            try {
              _startRealStream();
              return true;
            } catch (streamError) {
              _logger.w('Failed to start browser GPS stream: $streamError');
              locationFailure = 'Unable to start browser GPS tracking.';
            }
          } else {
            locationFailure = 'Location permission is required to go online.';
          }
        } else {
          locationFailure = 'Location services are disabled.';
        }
      } catch (e) {
        _logger.w('Browser geolocation check failed: $e');
        locationFailure = 'Unable to access browser location services.';
      }


      ref.read(gpsBufferServiceProvider).stopSyncTimer();
      state = LocationState(
        error: locationFailure ?? 'A valid GPS position is required.',
      );
      return false;
    }

    // ── สำหรับ Mobile App จริง (Android / iOS) ──────────────────────
    try {
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
      final position = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.high,
        ),
      );

      if (position.isMocked) {
        _logger.e('Mock GPS position detected on start!');
        state = state.copyWith(
          error: 'ตรวจพบการโกงตำแหน่งพิกัด (Mock GPS) ไม่อนุญาตให้ใช้แอปพลิเคชัน',
          isTracking: false,
        );
        return false;
      }

      if (position.accuracy <= 300.0) {
        state = LocationState(
          latitude: position.latitude,
          longitude: position.longitude,
          accuracy: position.accuracy,
          heading: _normalizeHeading(_readHeading(position)),
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
          'Initial GPS filtered: accuracy ${position.accuracy}m is > 300m',
        );
      }
    } catch (e) {
      _logger.e('❌ Failed to get initial position', error: e);
    }

    _startRealStream();
    return true;
  }

  void _startRealStream() {
    _positionSubscription?.cancel();
    _mockTimer?.cancel();
    _mockTimer = null;

    final locationSettings = buildLocationSettings(intervalSeconds: 10);

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
  }

  // Dynamic Settings Methods:
  LocationSettings buildLocationSettings({required int intervalSeconds}) {
    if (kIsWeb) {
      return LocationSettings(
        accuracy: LocationAccuracy.high,
        distanceFilter: Environment.gpsDistanceFilter,
      );
    } else if (defaultTargetPlatform == TargetPlatform.android) {
      return AndroidSettings(
        accuracy: LocationAccuracy.high,
        distanceFilter: Environment.gpsDistanceFilter,
        forceLocationManager: true,
        intervalDuration: Duration(seconds: intervalSeconds),
        foregroundNotificationConfig: const ForegroundNotificationConfig(
          notificationText: "แอปกำลังติดตามตำแหน่งของคุณเบื้องหลัง (สามารถกดยกเลิกการติดตามได้ในแอป)",
          notificationTitle: "Rider App เปิดใช้งาน GPS",
          enableWakeLock: true,
        ),
      );
    } else if (defaultTargetPlatform == TargetPlatform.iOS) {
      return AppleSettings(
        accuracy: LocationAccuracy.high,
        activityType: ActivityType.automotiveNavigation,
        distanceFilter: intervalSeconds > 10 ? 50 : Environment.gpsDistanceFilter,
        pauseLocationUpdatesAutomatically: true,
        showBackgroundLocationIndicator: true,
        allowBackgroundLocationUpdates: true,
      );
    } else {
      return LocationSettings(
        accuracy: LocationAccuracy.high,
        distanceFilter: Environment.gpsDistanceFilter,
      );
    }
  }

  void updateSettings(
    LocationSettings settings, {
    int? intervalSeconds,
  }) {
    if (!state.isTracking) return;
    if (kIsWeb && Environment.enableMockGps) {
      _startMockStream(
        intervalSeconds: intervalSeconds ?? _mockIntervalSeconds,
      );
      return;
    }

    _mockTimer?.cancel();
    _mockTimer = null;
    _positionSubscription?.cancel();
    _positionSubscription = Geolocator.getPositionStream(
      locationSettings: settings,
    ).listen(
      _onPositionUpdate,
      onError: (error) {
        _logger.e('❌ GPS stream error', error: error);
        state = state.copyWith(error: 'GPS tracking error: $error');
      },
    );
    _logger.i('🛰️ GPS tracking settings dynamically updated');
  }

  void _startMockStream({
    int intervalSeconds = Environment.gpsUpdateIntervalSeconds,
  }) {
    _positionSubscription?.cancel();
    _mockTimer?.cancel();
    _mockIntervalSeconds = intervalSeconds;

    _logger.i('🤖 Starting Mock GPS Stream for Web (Demo Mode)');
    
    // พิกัดศูนย์กลางอุดรธานี
    double currentLat = 17.4138;
    double currentLng = 102.7872;
    double angle = 0.0;

    state = LocationState(
      latitude: currentLat,
      longitude: currentLng,
      accuracy: 10.0,
      heading: 0.0,
      isTracking: true,
      lastUpdated: DateTime.now(),
    );

    // ส่งตำแหน่งเริ่มต้นทันที
    final bufferService = ref.read(gpsBufferServiceProvider);
    bufferService.bufferLocation(currentLat, currentLng, 10.0, heading: 0.0);

    // Simulate a small loop at the active GPS interval for map visibility.
    _mockTimer = Timer.periodic(Duration(seconds: _mockIntervalSeconds), (timer) {
      if (!state.isTracking) {
        timer.cancel();
        return;
      }
      
      // ขยับเล็กน้อย 0.0003 (~30 เมตร)
      angle += 0.1;
      final latOffset = 0.0003 * math.sin(angle);
      final lngOffset = 0.0003 * math.cos(angle);
      
      final nextLat = currentLat + latOffset;
      final nextLng = currentLng + lngOffset;

      final currentHeading = (angle * 180 / math.pi) % 360;
      state = state.copyWith(
        latitude: nextLat,
        longitude: nextLng,
        accuracy: 10.0,
        heading: currentHeading,
        lastUpdated: DateTime.now(),
      );

      bufferService.bufferLocation(nextLat, nextLng, 10.0, heading: currentHeading);
    });
  }

  /// หยุด GPS tracking.
  Future<void> stopTracking() async {
    ref.read(gpsBufferServiceProvider).stopSyncTimer();
    await _positionSubscription?.cancel();
    _positionSubscription = null;
    _mockTimer?.cancel();
    _mockTimer = null;
    state = state.copyWith(isTracking: false);
    _logger.i('🛑 GPS tracking stopped');
  }

  /// Handler สำหรับ position update.
  void _onPositionUpdate(Position position) {
    if (position.isMocked) {
      _logger.e('Mock GPS position detected during update!');
      state = state.copyWith(
        error: 'ตรวจพบการโกงตำแหน่งพิกัด (Mock GPS) ไม่อนุญาตให้ใช้งาน',
        isTracking: false,
      );
      stopTracking();
      // บังคับเปลี่ยนสถานะออฟไลน์
      Future.microtask(() {
        ref.read(riderSessionServiceProvider.notifier).goOffline();
      });
      return;
    }

    // ── 5. ตัวกรองพิกัด (Noise Filtering) ────────────────────────
    if (position.accuracy > 300.0) {
      _logger.d('🛑 GPS Noise filtered: accuracy ${position.accuracy}m is > 300m');
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

    final heading = _normalizeHeading(_readHeading(position));
    state = state.copyWith(
      latitude: avgLat,
      longitude: avgLng,
      accuracy: avgAccuracy,
      heading: heading,
      lastUpdated: DateTime.now(),
      error: null,
    );

    // ส่งพิกัดไปยัง Local DB Buffer สำหรับ Offline Buffering และ Batch Ingestion
    ref.read(gpsBufferServiceProvider).bufferLocation(avgLat, avgLng, avgAccuracy, heading: heading);
  }

  double? _normalizeHeading(double? heading) {
    if (heading == null || !heading.isFinite || heading < 0) return null;
    return heading % 360;
  }

  double? _readHeading(Position position) {
    try {
      final dynamic pos = position;
      return pos.heading as double?;
    } catch (_) {
      return null;
    }
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
