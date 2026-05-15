import 'package:freezed_annotation/freezed_annotation.dart';

part 'api_response.freezed.dart';
part 'api_response.g.dart';

/// Standard API Response wrapper.
///
/// ตรงกับ:
/// - .NET: `BackendApi/Core/Models/ApiResponse.cs`
/// - Angular: `admin-dashboard/src/app/models/common.model.ts` → `HttpStatusResult`
///
/// ```json
/// {
///   "Success": true,
///   "Message": "สำเร็จ",
///   "ErrorDetail": null,
///   "Code": null
/// }
/// ```
@freezed
abstract class ApiResponse with _$ApiResponse {
  const factory ApiResponse({
    /// ผลลัพธ์สำเร็จหรือไม่ (maps to `Success` in C# / `Success` in Angular)
    @JsonKey(name: 'Success') required bool success,

    /// ข้อความแสดงผล (maps to `Message`)
    @JsonKey(name: 'Message') String? message,

    /// รายละเอียด error (maps to `ErrorDetail` — แสดงเฉพาะ Dev mode)
    @JsonKey(name: 'ErrorDetail') String? errorDetail,

    /// Error code สำหรับ frontend mapping (maps to `Code`)
    @JsonKey(name: 'Code') String? code,
  }) = _ApiResponse;

  factory ApiResponse.fromJson(Map<String, dynamic> json) =>
      _$ApiResponseFromJson(json);
}

/// Standard API Response wrapper with payload.
///
/// ตรงกับ:
/// - .NET: `ApiResponse<T>` (inherits ApiResponse, adds `Value`)
/// - Angular: `HttpStatusResultValue<T>` (extends HttpStatusResult, adds `Value`)
///
/// ```json
/// {
///   "Success": true,
///   "Message": "สำเร็จ",
///   "Value": { ... }
/// }
/// ```
///
/// Usage:
/// ```dart
/// final response = ApiResponseValue<RiderDto>.fromJson(
///   json,
///   (obj) => RiderDto.fromJson(obj as Map<String, dynamic>),
/// );
/// ```
@Freezed(genericArgumentFactories: true)
abstract class ApiResponseValue<T> with _$ApiResponseValue<T> {
  const factory ApiResponseValue({
    @JsonKey(name: 'Success') required bool success,
    @JsonKey(name: 'Message') String? message,
    @JsonKey(name: 'ErrorDetail') String? errorDetail,
    @JsonKey(name: 'Code') String? code,
    @JsonKey(name: 'Value') T? value,
  }) = _ApiResponseValue<T>;

  factory ApiResponseValue.fromJson(
    Map<String, dynamic> json,
    T Function(Object?) fromJsonT,
  ) =>
      _$ApiResponseValueFromJson(json, fromJsonT);
}

/// Paginated result wrapper.
///
/// ตรงกับ: `BackendApi/Core/Models/PaginatedResult.cs`
@Freezed(genericArgumentFactories: true)
abstract class PaginatedResult<T> with _$PaginatedResult<T> {
  const factory PaginatedResult({
    @JsonKey(name: 'Items') required List<T> items,
    @JsonKey(name: 'TotalCount') required int totalCount,
    @JsonKey(name: 'Page') required int page,
    @JsonKey(name: 'PageSize') required int pageSize,
    @JsonKey(name: 'HasPrevious') required bool hasPrevious,
    @JsonKey(name: 'HasNext') required bool hasNext,
  }) = _PaginatedResult<T>;

  factory PaginatedResult.fromJson(
    Map<String, dynamic> json,
    T Function(Object?) fromJsonT,
  ) =>
      _$PaginatedResultFromJson(json, fromJsonT);
}
