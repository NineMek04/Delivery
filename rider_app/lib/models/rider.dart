import 'package:freezed_annotation/freezed_annotation.dart';

part 'rider.freezed.dart';
part 'rider.g.dart';

/// Rider DTO — ข้อมูลไรเดอร์.
///
/// ตรงกับ:
/// - .NET Entity: `BackendApi/Models/Rider.cs`
/// - .NET DTO: `BackendApi/Models/DTOs/RiderDto.cs`
///
/// Fields mapping:
/// | Flutter (Dart)     | .NET (C#)           |
/// |--------------------|---------------------|
/// | id                 | Id (GUID string)    |
/// | name               | Name                |
/// | status             | Status              |
/// | lat                | Lat (from PostGIS)  |
/// | lng                | Lng (from PostGIS)  |
/// | lastUpdated        | LastUpdated         |
@freezed
abstract class RiderDto with _$RiderDto {
  const factory RiderDto({
    /// รหัสไรเดอร์ (GUID)
    @JsonKey(name: 'Id') required String id,

    /// ชื่อไรเดอร์
    @JsonKey(name: 'Name') required String name,

    /// สถานะ: AVAILABLE, DELIVERING, OFFLINE
    @JsonKey(name: 'Status') @Default('AVAILABLE') String status,

    /// ละติจูดปัจจุบัน (mapped from PostGIS Point → lat/lng by Mapster)
    @JsonKey(name: 'Lat') double? lat,

    /// ลองจิจูดปัจจุบัน
    @JsonKey(name: 'Lng') double? lng,

    /// เวลาที่อัปเดตตำแหน่งล่าสุด
    @JsonKey(name: 'LastUpdated') DateTime? lastUpdated,
  }) = _RiderDto;

  factory RiderDto.fromJson(Map<String, dynamic> json) =>
      _$RiderDtoFromJson(json);
}

/// Create/Update Rider DTO.
///
/// ตรงกับ: `BackendApi/Models/DTOs/CreateRiderDto.cs`
@freezed
abstract class CreateRiderDto with _$CreateRiderDto {
  const factory CreateRiderDto({
    /// ชื่อไรเดอร์
    @JsonKey(name: 'Name') required String name,

    /// ละติจูดเริ่มต้น (ไม่บังคับ)
    @JsonKey(name: 'Lat') double? lat,

    /// ลองจิจูดเริ่มต้น (ไม่บังคับ)
    @JsonKey(name: 'Lng') double? lng,
  }) = _CreateRiderDto;

  factory CreateRiderDto.fromJson(Map<String, dynamic> json) =>
      _$CreateRiderDtoFromJson(json);
}
