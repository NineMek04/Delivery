import 'dart:async';
import 'dart:math';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:latlong2/latlong.dart';

import '../../../core/signalr/signalr_service.dart';

final simulatedJourneyProvider = Provider<SimulatedJourneyService>((ref) {
  return SimulatedJourneyService(ref);
});

class SimulatedJourneyService {
  final Ref _ref;
  Timer? _timer;
  List<LatLng> _currentRoute = [];
  int _currentIndex = 0;
  bool _isRunning = false;
  
  Function(double distanceToTarget)? onDistanceUpdated;
  Function()? onDestinationReached;

  SimulatedJourneyService(this._ref);

  void startJourney({
    required List<LatLng> routeCoords,
    required LatLng destination,
    required StateController<LatLng> locationStateController,
  }) {
    stopJourney();
    if (routeCoords.isEmpty) return;

    // Interpolate coordinates for smooth 300ms steps (e.g. ~12 meters per step)
    _currentRoute = _interpolateCoordinates(routeCoords, 12.0);
    _currentIndex = 0;
    _isRunning = true;

    _timer = Timer.periodic(const Duration(milliseconds: 300), (timer) {
      if (_currentIndex >= _currentRoute.length) {
        stopJourney();
        if (onDestinationReached != null) {
          onDestinationReached!();
        }
        return;
      }

      final currentLocation = _currentRoute[_currentIndex];
      
      // Update local state (UI Map)
      locationStateController.state = currentLocation;

      // Send to Backend
      final signalR = _ref.read(signalRServiceProvider.notifier);
      signalR.updateLocation(
        currentLocation.latitude, 
        currentLocation.longitude, 
        5.0, // simulated GPS accuracy in meters
      );

      // Calculate distance to destination
      final distance = const Distance().as(
        LengthUnit.Meter, 
        currentLocation, 
        destination,
      );

      if (onDistanceUpdated != null) {
        onDistanceUpdated!(distance);
      }

      _currentIndex++;
    });
  }

  void stopJourney() {
    _isRunning = false;
    _timer?.cancel();
    _timer = null;
  }

  bool get isRunning => _isRunning;

  List<LatLng> decodePolyline(String encoded) {
    List<LatLng> poly = [];
    int index = 0;
    int len = encoded.length;
    int lat = 0, lng = 0;

    while (index < len) {
      int b, shift = 0, result = 0;
      do {
        b = encoded.codeUnitAt(index++) - 63;
        result |= (b & 0x1f) << shift;
        shift += 5;
      } while (b >= 0x20);
      int dlat = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
      lat += dlat;

      shift = 0;
      result = 0;
      do {
        b = encoded.codeUnitAt(index++) - 63;
        result |= (b & 0x1f) << shift;
        shift += 5;
      } while (b >= 0x20);
      int dlng = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
      lng += dlng;

      poly.add(LatLng((lat / 1E5).toDouble(), (lng / 1E5).toDouble()));
    }
    return poly;
  }

  List<LatLng> _interpolateCoordinates(List<LatLng> coords, double stepDistanceMeters) {
    final interpolated = <LatLng>[];
    if (coords.isEmpty) return interpolated;
    interpolated.add(coords.first);
    
    final distanceCalc = const Distance();

    for (int i = 0; i < coords.length - 1; i++) {
      final start = coords[i];
      final end = coords[i + 1];
      
      final dist = distanceCalc.as(LengthUnit.Meter, start, end);
      final numSteps = max(1, (dist / stepDistanceMeters).floor());
      
      final latDiff = end.latitude - start.latitude;
      final lngDiff = end.longitude - start.longitude;

      for (int j = 1; j <= numSteps; j++) {
        final t = j / numSteps;
        interpolated.add(LatLng(
          start.latitude + latDiff * t,
          start.longitude + lngDiff * t,
        ));
      }
    }
    return interpolated;
  }
}
