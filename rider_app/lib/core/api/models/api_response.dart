import '../api_helpers.dart';

/// Standard API Response wrapper (`ApiResponse` / `ApiResponse<T>`).
class ApiResponse {
  final int? status;
  final bool success;
  final String? message;
  final String? errorDetail;
  final String? code;

  const ApiResponse({
    this.status,
    required this.success,
    this.message,
    this.errorDetail,
    this.code,
  });

  factory ApiResponse.fromJson(Map<String, dynamic> json) {
    return ApiResponse(
      status: readField<num>(json, 'Status')?.toInt(),
      success: readField<bool>(json, 'Success') ?? false,
      message: readField<String>(json, 'Message'),
      errorDetail: readField<String>(json, 'ErrorDetail'),
      code: readField<String>(json, 'Code'),
    );
  }
}

class ApiResponseValue<T> {
  final int? status;
  final bool success;
  final String? message;
  final String? errorDetail;
  final String? code;
  final T? value;

  const ApiResponseValue({
    this.status,
    required this.success,
    this.message,
    this.errorDetail,
    this.code,
    this.value,
  });

  factory ApiResponseValue.fromJson(
    Map<String, dynamic> json,
    T Function(Map<String, dynamic>) fromJsonT,
  ) {
    final rawValue = readField<dynamic>(json, 'Value') ?? readField<dynamic>(json, 'value');
    return ApiResponseValue(
      status: readField<num>(json, 'Status')?.toInt(),
      success: readField<bool>(json, 'Success') ?? false,
      message: readField<String>(json, 'Message'),
      errorDetail: readField<String>(json, 'ErrorDetail'),
      code: readField<String>(json, 'Code'),
      value: rawValue == null ? null : fromJsonT(asMap(rawValue)),
    );
  }
}

class PaginatedResult<T> {
  final List<T> items;
  final int totalCount;
  final int page;
  final int pageSize;
  final bool hasPrevious;
  final bool hasNext;

  const PaginatedResult({
    required this.items,
    required this.totalCount,
    required this.page,
    required this.pageSize,
    required this.hasPrevious,
    required this.hasNext,
  });

  factory PaginatedResult.fromJson(
    Map<String, dynamic> json,
    T Function(Map<String, dynamic>) fromJsonT,
  ) {
    final rawItems = readField<List>(json, 'Items') ?? readField<List>(json, 'items') ?? [];
    return PaginatedResult(
      items: rawItems.map((e) => fromJsonT(asMap(e))).toList(),
      totalCount:
          readField<num>(json, 'TotalCount')?.toInt() ??
          readField<num>(json, 'totalCount')?.toInt() ??
          0,
      page:
          readField<num>(json, 'Page')?.toInt() ??
          readField<num>(json, 'page')?.toInt() ??
          1,
      pageSize:
          readField<num>(json, 'PageSize')?.toInt() ??
          readField<num>(json, 'pageSize')?.toInt() ??
          10,
      hasPrevious:
          readField<bool>(json, 'HasPrevious') ??
          readField<bool>(json, 'hasPrevious') ??
          false,
      hasNext:
          readField<bool>(json, 'HasNext') ??
          readField<bool>(json, 'hasNext') ??
          false,
    );
  }
}
