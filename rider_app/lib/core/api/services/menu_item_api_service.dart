import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../models/shop.dart';
import '../api_helpers.dart';
import '../models/api_response.dart';
import '../delivery_api_client.dart';

final menuItemApiServiceProvider = Provider<MenuItemApiService>((ref) {
  return MenuItemApiService(ref.watch(deliveryApiClientProvider));
});

/// REST client for `/api/v1/menuitems/*`.
class MenuItemApiService {
  final Dio _dio;

  MenuItemApiService(this._dio);

  /// Get menu items for a specific shop (paginated).
  Future<PaginatedResult<MenuItemDto>> getByShop(
    String shopId, {
    int page = 1,
    int pageSize = 100,
  }) async {
    try {
      final response = await _dio.get(
        'menuitems/shop/$shopId',
        queryParameters: {'page': page, 'pageSize': pageSize},
      );
      final parsed = parseApiResponse(
        response.data,
        (json) => PaginatedResult.fromJson(asMap(json), MenuItemDto.fromJson),
      );
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  /// Create a new menu item.
  Future<MenuItemDto> create(Map<String, dynamic> data) async {
    try {
      final response = await _dio.post('menuitems', data: data);
      final parsed = parseApiResponse(response.data, MenuItemDto.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  /// Update an existing menu item.
  Future<MenuItemDto> update(String id, Map<String, dynamic> data) async {
    try {
      final response = await _dio.put('menuitems/$id', data: data);
      final parsed = parseApiResponse(response.data, MenuItemDto.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  /// Delete a menu item (soft delete).
  /// Backend returns 204 No Content.
  Future<void> delete(String id) async {
    try {
      await _dio.delete('menuitems/$id');
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  /// Get all menu items across all shops (paginated).
  Future<PaginatedResult<MenuItemDto>> getAll({
    String? search,
    int page = 1,
    int pageSize = 20,
  }) async {
    try {
      final response = await _dio.get(
        'menuitems',
        queryParameters: {
          if (search != null && search.isNotEmpty) 'search': search,
          'page': page,
          'pageSize': pageSize,
        },
      );
      final parsed = parseApiResponse(
        response.data,
        (json) => PaginatedResult.fromJson(asMap(json), MenuItemDto.fromJson),
      );
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }
}
