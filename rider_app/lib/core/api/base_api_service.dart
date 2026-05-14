import 'package:dio/dio.dart';

import 'models/api_response.dart';

/// Generic Base API Service — CRUD operations สำหรับทุก entity.
///
/// เทียบกับ:
/// - Angular: `admin-dashboard/src/app/core/services/base-api.service.ts`
/// - .NET: `BackendApi/Core/CrudControllerBase.cs`
///
/// ```typescript
/// // Angular version:
/// export abstract class BaseApiService<T> {
///   protected abstract get endpoint(): string;
///   public getAll(): Observable<T[]> { ... }
///   public getById(id: string | number): Observable<T> { ... }
/// }
/// ```
///
/// Usage:
/// ```dart
/// class RiderService extends BaseApiService<RiderDto> {
///   RiderService(super.dio);
///   @override String get endpoint => '/riders';
///   @override RiderDto fromJson(Map<String, dynamic> json) => RiderDto.fromJson(json);
/// }
/// ```
abstract class BaseApiService<T> {
  final Dio dio;

  BaseApiService(this.dio);

  /// API endpoint path (เทียบ Angular: `protected abstract get endpoint(): string`)
  String get endpoint;

  /// JSON → Entity converter
  T fromJson(Map<String, dynamic> json);

  /// GET all items.
  /// เทียบ Angular: `getAll(): Observable<T[]>`
  /// เทียบ .NET: `CrudControllerBase.GetAll()`
  Future<ApiResponseValue<List<T>>> getAll({
    Map<String, dynamic>? queryParameters,
  }) async {
    final response = await dio.get(
      endpoint,
      queryParameters: queryParameters,
    );

    return ApiResponseValue.fromJson(
      response.data as Map<String, dynamic>,
      (json) => (json as List)
          .map((e) => fromJson(e as Map<String, dynamic>))
          .toList(),
    );
  }

  /// GET item by ID.
  /// เทียบ Angular: `getById(id): Observable<T>`
  /// เทียบ .NET: `CrudControllerBase.GetById(key)`
  Future<ApiResponseValue<T>> getById(dynamic id) async {
    final response = await dio.get('$endpoint/$id');

    return ApiResponseValue.fromJson(
      response.data as Map<String, dynamic>,
      (json) => fromJson(json as Map<String, dynamic>),
    );
  }

  /// POST create new item.
  /// เทียบ Angular: `create(data): Observable<T>`
  /// เทียบ .NET: `CrudControllerBase.Create(dto)`
  Future<ApiResponseValue<T>> create(Map<String, dynamic> data) async {
    final response = await dio.post(endpoint, data: data);

    return ApiResponseValue.fromJson(
      response.data as Map<String, dynamic>,
      (json) => fromJson(json as Map<String, dynamic>),
    );
  }

  /// PUT update existing item.
  /// เทียบ Angular: `update(id, data): Observable<T>`
  /// เทียบ .NET: `CrudControllerBase.Update(key, dto)`
  Future<ApiResponseValue<T>> update(
    dynamic id,
    Map<String, dynamic> data,
  ) async {
    final response = await dio.put('$endpoint/$id', data: data);

    return ApiResponseValue.fromJson(
      response.data as Map<String, dynamic>,
      (json) => fromJson(json as Map<String, dynamic>),
    );
  }

  /// DELETE item by ID.
  /// เทียบ Angular: `delete(id): Observable<any>`
  /// เทียบ .NET: `CrudControllerBase.Delete(key)`
  Future<ApiResponse> delete(dynamic id) async {
    final response = await dio.delete('$endpoint/$id');

    return ApiResponse.fromJson(response.data as Map<String, dynamic>);
  }
}
