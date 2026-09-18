import 'dart:math' as math;
import 'package:flutter/foundation.dart';
import 'package:geolocator/geolocator.dart';
import '../config/environment.dart';

class LocationSettingsHelper {
  static LocationSettings buildLocationSettings({required int intervalSeconds}) {
    if (kIsWeb) {
      return LocationSettings(
        accuracy: LocationAccuracy.high,
        distanceFilter: Environment.gpsDistanceFilter,
      );
    } else if (defaultTargetPlatform == TargetPlatform.android) {
      return AndroidSettings(
        accuracy: LocationAccuracy.high,
        distanceFilter: 2,
        forceLocationManager: false,
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
        distanceFilter: 2,
        pauseLocationUpdatesAutomatically: false,
        showBackgroundLocationIndicator: true,
        allowBackgroundLocationUpdates: true,
      );
    } else {
      return const LocationSettings(
        accuracy: LocationAccuracy.high,
        distanceFilter: 2,
      );
    }
  }

  static double calculateBearing(double startLat, double startLng, double endLat, double endLng) {
    final lat1 = startLat * math.pi / 180;
    final lng1 = startLng * math.pi / 180;
    final lat2 = endLat * math.pi / 180;
    final lng2 = endLng * math.pi / 180;
    final dLng = lng2 - lng1;
    final y = math.sin(dLng) * math.cos(lat2);
    final x = math.cos(lat1) * math.sin(lat2) - math.sin(lat1) * math.cos(lat2) * math.cos(dLng);
    final brng = math.atan2(y, x);
    return (brng * 180 / math.pi + 360) % 360;
  }

  static double? normalizeHeading(double? heading) {
    if (heading == null || !heading.isFinite || heading < 0) return null;
    return heading % 360;
  }

  static double? readHeading(Position position) {
    try {
      final dynamic pos = position;
      return pos.heading as double?;
    } catch (_) {
      return null;
    }
  }
}
