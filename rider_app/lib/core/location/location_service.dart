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
import '../signalr/signalr_service.dart';
import 'location_state.dart';
import 'location_settings_helper.dart';
import 'location_permission_helper.dart';

export 'location_state.dart';
export 'location_settings_helper.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

/// Location Service — GPS tracking สำหรับ Rider.
///
/// นี่คือหัวใจของ Rider App:
/// 1. ดึงตำแหน่ง GPS ของ Rider แบบ real-time
/// 2. ส่งพิกัดผ่าน SignalR → .NET Backend → PostgreSQL/PostGIS
/// 3. Backend broadcast ไปยัง Angular Dashboard (admin-dashboard)
class LocationService extends Notifier<LocationState> {
  StreamSubscription<Position>? _positionSubscription;
  Timer? _mockTimer;
  int _mockIntervalSeconds = Environment.gpsUpdateIntervalSeconds;

  // ฟิลด์ระดับคลาสสำหรับตัวจำลอง Mock GPS เพื่อป้องกันไม่ให้พิกัดดีดกลับจุดเริ่มต้น
  double _mockLat = 17.4138;
  double _mockLng = 102.7872;
  double _mockAngle = 0.0;

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

      final webResult = await LocationPermissionHelper.checkAndRequestWeb();
      if (webResult.success && webResult.initialPosition != null) {
        final position = webResult.initialPosition!;
        state = LocationState(
          latitude: position.latitude,
          longitude: position.longitude,
          accuracy: position.accuracy,
          heading: LocationSettingsHelper.normalizeHeading(
            LocationSettingsHelper.readHeading(position),
          ),
          isTracking: true,
          lastUpdated: DateTime.now(),
        );

        try {
          _startRealStream();
          return true;
        } catch (streamError) {
          _logger.w('Failed to start browser GPS stream: $streamError');
        }
      }

      ref.read(gpsBufferServiceProvider).stopSyncTimer();
      state = LocationState(
        error: webResult.errorMessage ?? 'A valid GPS position is required.',
      );
      return false;
    }

    // ── สำหรับ Mobile App จริง (Android / iOS) ──────────────────────
    final mobileResult = await LocationPermissionHelper.checkAndRequestMobile();
    if (!mobileResult.success) {
      ref.read(gpsBufferServiceProvider).stopSyncTimer();
      state = state.copyWith(
        error: mobileResult.errorMessage,
        isTracking: false,
      );
      return false;
    }

    final position = mobileResult.initialPosition;
    if (position != null) {
      if (position.accuracy <= 300.0) {
        state = LocationState(
          latitude: position.latitude,
          longitude: position.longitude,
          accuracy: position.accuracy,
          heading: LocationSettingsHelper.normalizeHeading(
            LocationSettingsHelper.readHeading(position),
          ),
          isTracking: true,
          lastUpdated: DateTime.now(),
        );
        _logger.i(
          'Initial GPS accepted: ${position.latitude}, ${position.longitude} (${position.accuracy}m)',
        );
      } else {
        state = const LocationState(isTracking: true);
        _logger.d('Initial GPS filtered: accuracy ${position.accuracy}m is > 300m');
      }
    }

    _startRealStream();
    return true;
  }

  void _startRealStream() {
    _positionSubscription?.cancel();
    _mockTimer?.cancel();
    _mockTimer = null;

    final locationSettings = buildLocationSettings(intervalSeconds: Environment.gpsUpdateIntervalSeconds);

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

  LocationSettings buildLocationSettings({required int intervalSeconds}) {
    return LocationSettingsHelper.buildLocationSettings(intervalSeconds: intervalSeconds);
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
    
    if (state.latitude != null && state.longitude != null) {
      _mockLat = state.latitude!;
      _mockLng = state.longitude!;
    }

    state = LocationState(
      latitude: _mockLat,
      longitude: _mockLng,
      accuracy: 10.0,
      heading: state.heading ?? 0.0,
      isTracking: true,
      lastUpdated: DateTime.now(),
    );

    final bufferService = ref.read(gpsBufferServiceProvider);
    unawaited(ref
        .read(signalRServiceProvider.notifier)
        .updateLocation(_mockLat, _mockLng, 10.0));
    bufferService.bufferLocation(_mockLat, _mockLng, 10.0, heading: state.heading ?? 0.0);

    _mockTimer = Timer.periodic(Duration(seconds: _mockIntervalSeconds), (timer) {
      if (!state.isTracking) {
        timer.cancel();
        return;
      }
      if (state.isAutoDrive) {
        return;
      }
      
      _mockAngle += 0.1;
      final latOffset = 0.0003 * math.sin(_mockAngle);
      final lngOffset = 0.0003 * math.cos(_mockAngle);
      
      _mockLat = _mockLat + latOffset;
      _mockLng = _mockLng + lngOffset;

      final currentHeading = (_mockAngle * 180 / math.pi) % 360;
      state = state.copyWith(
        latitude: _mockLat,
        longitude: _mockLng,
        accuracy: 10.0,
        heading: currentHeading,
        lastUpdated: DateTime.now(),
      );

      bufferService.bufferLocation(_mockLat, _mockLng, 10.0, heading: currentHeading);
      unawaited(ref
          .read(signalRServiceProvider.notifier)
          .updateLocation(_mockLat, _mockLng, 10.0));
    });
  }

  void setAutoDriveEnabled(bool enabled) {
    state = state.copyWith(isAutoDrive: enabled);
    _logger.i('🚗 In-App Drive Simulation state in LocationService changed to: $enabled');
  }

  void updateStatePosition(double lat, double lng, double heading) {
    _mockLat = lat;
    _mockLng = lng;
    _mockAngle = (heading * math.pi / 180);

    state = state.copyWith(
      latitude: lat,
      longitude: lng,
      accuracy: 10.0,
      heading: heading,
      lastUpdated: DateTime.now(),
    );
    _logger.i('📍 Manually updated geolocator state position to: $lat, $lng');
  }

  Future<void> stopTracking() async {
    ref.read(gpsBufferServiceProvider).stopSyncTimer();
    await _positionSubscription?.cancel();
    _positionSubscription = null;
    _mockTimer?.cancel();
    _mockTimer = null;
    state = state.copyWith(isTracking: false);
    _logger.i('🛑 GPS tracking stopped');
  }

  void _onPositionUpdate(Position position) {
    if (state.isAutoDrive) {
      return;
    }
    if (position.isMocked) {
      _logger.e('Mock GPS position detected during update!');
      state = state.copyWith(
        error: 'ตรวจพบการโกงตำแหน่งพิกัด (Mock GPS) ไม่อนุญาตให้ใช้งาน',
        isTracking: false,
      );
      stopTracking();
      Future.microtask(() {
        ref.read(riderSessionServiceProvider.notifier).goOffline();
      });
      return;
    }

    if (position.accuracy > 300.0) {
      _logger.d('🛑 GPS Noise filtered: accuracy ${position.accuracy}m is > 300m');
      return;
    }

    const double alpha = 0.6;
    double emaLat;
    double emaLng;
    double emaAccuracy;

    if (state.latitude != null && state.longitude != null) {
      emaLat = alpha * position.latitude + (1 - alpha) * state.latitude!;
      emaLng = alpha * position.longitude + (1 - alpha) * state.longitude!;
      emaAccuracy = alpha * position.accuracy + (1 - alpha) * (state.accuracy ?? position.accuracy);
    } else {
      emaLat = position.latitude;
      emaLng = position.longitude;
      emaAccuracy = position.accuracy;
    }

    double? heading = LocationSettingsHelper.normalizeHeading(
      LocationSettingsHelper.readHeading(position),
    );
    if ((heading == null || heading == 0.0) && state.latitude != null && state.longitude != null) {
      final dist = Geolocator.distanceBetween(state.latitude!, state.longitude!, emaLat, emaLng);
      if (dist >= 1.5) {
        heading = LocationSettingsHelper.calculateBearing(state.latitude!, state.longitude!, emaLat, emaLng);
      } else {
        heading = state.heading;
      }
    }

    state = state.copyWith(
      latitude: emaLat,
      longitude: emaLng,
      accuracy: emaAccuracy,
      heading: heading,
      lastUpdated: DateTime.now(),
      error: null,
    );

    unawaited(ref
        .read(signalRServiceProvider.notifier)
        .updateLocation(emaLat, emaLng, emaAccuracy));

    ref.read(gpsBufferServiceProvider).bufferLocation(emaLat, emaLng, emaAccuracy, heading: heading);
  }
}

final locationServiceProvider = NotifierProvider<LocationService, LocationState>(
  LocationService.new,
);
