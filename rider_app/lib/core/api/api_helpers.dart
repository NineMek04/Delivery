import 'package:dio/dio.dart';

import 'models/api_response.dart';

/// Thrown when BackendApi returns Success=false or HTTP error.
class ApiException implements Exception {
  final String message;
  final int? statusCode;
  final String? code;

  const ApiException(this.message, {this.statusCode, this.code});

  @override
  String toString() => 'ApiException($statusCode): $message';
}

/// Read a field supporting PascalCase and camelCase JSON from .NET.
T? readField<T>(Map<String, dynamic> json, String pascalKey) {
  if (json.containsKey(pascalKey)) return json[pascalKey] as T?;
  final camelKey = _toCamelCase(pascalKey);
  if (json.containsKey(camelKey)) return json[camelKey] as T?;
  return null;
}

String _toCamelCase(String key) {
  if (key.isEmpty) return key;
  return key[0].toLowerCase() + key.substring(1);
}

Map<String, dynamic> asMap(dynamic value) {
  if (value is Map<String, dynamic>) return value;
  if (value is Map) return Map<String, dynamic>.from(value);
  throw ApiException('Expected JSON object but got ${value.runtimeType}');
}

ApiResponseValue<T> parseApiResponse<T>(
  dynamic data,
  T Function(Map<String, dynamic>) fromJson,
) {
  final json = asMap(data);
  return ApiResponseValue.fromJson(json, (obj) {
    if (obj == null) throw const ApiException('Response Value is null');
    return fromJson(asMap(obj));
  });
}

ApiResponseValue<List<T>> parseApiListResponse<T>(
  dynamic data,
  T Function(Map<String, dynamic>) fromJson,
) {
  final json = asMap(data);
  final rawValue =
      readField<dynamic>(json, 'Value') ?? readField<dynamic>(json, 'value');
  if (rawValue is! List) {
    throw ApiException('Expected list but got ${rawValue.runtimeType}');
  }

  return ApiResponseValue(
    status: readField<num>(json, 'Status')?.toInt(),
    success: readField<bool>(json, 'Success') ?? false,
    message: readField<String>(json, 'Message'),
    errorDetail: readField<String>(json, 'ErrorDetail'),
    code: readField<String>(json, 'Code'),
    value: rawValue.map((e) => fromJson(asMap(e))).toList(),
  );
}

void ensureSuccess<T>(ApiResponseValue<T> response) {
  if (!response.success) {
    throw ApiException(
      response.message ?? response.errorDetail ?? 'Request failed',
      statusCode: response.status,
      code: response.code,
    );
  }
  if (response.value == null) {
    throw const ApiException('Response has no value');
  }
}

DioException wrapDioError(DioException error) {
  final data = error.response?.data;
  if (data is Map) {
    final map = asMap(data);
    final message = readField<String>(map, 'Message') ??
        readField<String>(map, 'ErrorDetail') ??
        error.message;
    return DioException(
      requestOptions: error.requestOptions,
      response: error.response,
      type: error.type,
      error: ApiException(
        message ?? 'Request failed',
        statusCode: error.response?.statusCode,
        code: readField<String>(map, 'Code'),
      ),
    );
  }
  return error;
}
