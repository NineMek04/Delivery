/// สถานะ GPS location ของ Rider.
class LocationState {
  final double? latitude;
  final double? longitude;
  final double? accuracy;
  final double? heading;
  final bool isTracking;
  final bool isAutoDrive;
  final DateTime? lastUpdated;
  final String? error;

  const LocationState({
    this.latitude,
    this.longitude,
    this.accuracy,
    this.heading,
    this.isTracking = false,
    this.isAutoDrive = false,
    this.lastUpdated,
    this.error,
  });

  LocationState copyWith({
    double? latitude,
    double? longitude,
    double? accuracy,
    double? heading,
    bool? isTracking,
    bool? isAutoDrive,
    DateTime? lastUpdated,
    String? error,
  }) {
    return LocationState(
      latitude: latitude ?? this.latitude,
      longitude: longitude ?? this.longitude,
      accuracy: accuracy ?? this.accuracy,
      heading: heading ?? this.heading,
      isTracking: isTracking ?? this.isTracking,
      isAutoDrive: isAutoDrive ?? this.isAutoDrive,
      lastUpdated: lastUpdated ?? this.lastUpdated,
      error: error,
    );
  }
}
