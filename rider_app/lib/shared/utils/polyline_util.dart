import 'package:latlong2/latlong.dart';

/// Decode Google encoded polyline → list of LatLng.
List<LatLng> decodePolyline(String encoded) {
  if (encoded.isEmpty) return [];

  final decoded = _decodePolyline(encoded, precision: 1e5);
  if (decoded.length >= 2) return decoded;

  // Some route providers return polyline6. Keep this fallback so the map does
  // not silently lose routes if backend geometry settings change.
  return _decodePolyline(encoded, precision: 1e6);
}

List<LatLng> _decodePolyline(String encoded, {required double precision}) {
  try {
    final points = <LatLng>[];
    var index = 0;
    var lat = 0;
    var lng = 0;

    int readValue() {
      var shift = 0;
      var result = 0;
      int b;

      do {
        if (index >= encoded.length) {
          throw const FormatException('Invalid polyline');
        }

        b = encoded.codeUnitAt(index++) - 63;
        result |= (b & 0x1f) << shift;
        shift += 5;
      } while (b >= 0x20);

      return (result & 1) != 0 ? ~(result >> 1) : (result >> 1);
    }

    while (index < encoded.length) {
      lat += readValue();
      lng += readValue();

      final decodedLat = lat / precision;
      final decodedLng = lng / precision;

      if (decodedLat < -90 ||
          decodedLat > 90 ||
          decodedLng < -180 ||
          decodedLng > 180) {
        return const [];
      }

      points.add(LatLng(decodedLat, decodedLng));
    }

    return points;
  } catch (_) {
    return const [];
  }
}
