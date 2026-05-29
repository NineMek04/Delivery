import 'package:isar/isar.dart';

part 'gps_point.g.dart';

/// Isar collection representing offline-buffered GPS coordinates.
@collection
class GpsPoint {
  Id id = Isar.autoIncrement;

  double? latitude;
  double? longitude;
  double? accuracy;
  String? timestamp;
}
