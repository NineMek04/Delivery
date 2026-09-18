import 'package:flutter/foundation.dart';
import 'package:geolocator/geolocator.dart';
import 'package:logger/logger.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

class PermissionCheckResult {
  final bool success;
  final String? errorMessage;
  final Position? initialPosition;

  const PermissionCheckResult({
    required this.success,
    this.errorMessage,
    this.initialPosition,
  });
}

class LocationPermissionHelper {
  static Future<PermissionCheckResult> checkAndRequestWeb() async {
    try {
      final serviceEnabled = await Geolocator.isLocationServiceEnabled();
      if (!serviceEnabled) {
        return const PermissionCheckResult(
          success: false,
          errorMessage: 'Location services are disabled.',
        );
      }

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
        return PermissionCheckResult(success: true, initialPosition: position);
      } else {
        return const PermissionCheckResult(
          success: false,
          errorMessage: 'Location permission is required to go online.',
        );
      }
    } catch (e) {
      _logger.w('Browser geolocation check failed: $e');
      return const PermissionCheckResult(
        success: false,
        errorMessage: 'Unable to access browser location services.',
      );
    }
  }

  static Future<PermissionCheckResult> checkAndRequestMobile() async {
    try {
      final serviceEnabled = await Geolocator.isLocationServiceEnabled();
      if (!serviceEnabled) {
        _logger.w('📍 Location services are disabled');
        return const PermissionCheckResult(
          success: false,
          errorMessage: 'Location services are disabled. Please enable GPS.',
        );
      }

      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
        if (permission == LocationPermission.denied) {
          _logger.w('📍 Location permission denied');
          return const PermissionCheckResult(
            success: false,
            errorMessage: 'Location permission denied.',
          );
        }
      }

      if (permission == LocationPermission.deniedForever) {
        _logger.w('📍 Location permission permanently denied');
        return const PermissionCheckResult(
          success: false,
          errorMessage: 'Location permission permanently denied. Please enable in Settings.',
        );
      }

      final position = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.high,
        ),
      );

      if (position.isMocked) {
        _logger.e('Mock GPS position detected on start!');
        return const PermissionCheckResult(
          success: false,
          errorMessage: 'ตรวจพบการโกงตำแหน่งพิกัด (Mock GPS) ไม่อนุญาตให้ใช้แอปพลิเคชัน',
        );
      }

      return PermissionCheckResult(success: true, initialPosition: position);
    } catch (e) {
      _logger.e('❌ Failed to get initial position', error: e);
      return PermissionCheckResult(success: false, errorMessage: 'Failed to get initial position: $e');
    }
  }
}
