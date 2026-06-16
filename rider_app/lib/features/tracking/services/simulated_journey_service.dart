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
  List<double> _remainingDistances = [];

  List<LatLng> get currentRoute => _currentRoute;
  int get currentIndex => _currentIndex;
  bool get isRunning => _isRunning;
  
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

    // Interpolate coordinates for 3-second steps (e.g. ~30 meters per step)
    _currentRoute = _interpolateCoordinates(routeCoords, 30.0);
    _currentIndex = 0;
    _isRunning = true;

    // Precalculate remaining distances along the route O(N)
    _remainingDistances = List<double>.filled(_currentRoute.length, 0.0);
    double accum = 0.0;
    final distanceCalc = const Distance();
    for (int i = _currentRoute.length - 2; i >= 0; i--) {
      accum += distanceCalc.as(
        LengthUnit.Meter,
        _currentRoute[i],
        _currentRoute[i + 1],
      );
      _remainingDistances[i] = accum;
    }

    _timer = Timer.periodic(const Duration(seconds: 3), (timer) {
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

      // Get distance in O(1) time
      double distance = 0.0;
      if (_currentIndex < _remainingDistances.length) {
        distance = _remainingDistances[_currentIndex];
      }

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
      
      // Skip invalid placeholder coordinates (e.g. 0.0, 0.0) which cause massive step counts
      if (start.latitude == 0.0 || start.longitude == 0.0 || end.latitude == 0.0 || end.longitude == 0.0) {
        continue;
      }
      
      final dist = distanceCalc.as(LengthUnit.Meter, start, end);
      int numSteps = (dist / stepDistanceMeters).floor();
      if (numSteps > 150) {
        numSteps = 150; // Cap steps per segment to prevent UI thread lockup on huge distances
      }
      numSteps = max(1, numSteps);
      
      final latDiff = end.latitude - start.latitude;
      final lngDiff = end.longitude - start.longitude;

      for (int j = 1; j <= numSteps; j++) {
        final t = j / numSteps;
        interpolated.add(LatLng(
          start.latitude + latDiff * t,
          start.longitude + lngDiff * t,
        ));
      }

      if (interpolated.length > 3000) {
        break; // Cap total points to prevent memory bloat/lag
      }
    }
    return interpolated;
  }
}
