import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../models/shop.dart';
import '../api_helpers.dart';
import '../models/api_response.dart';
import '../delivery_api_client.dart';

final menuCategoryApiServiceProvider = Provider<MenuCategoryApiService>((ref) {
  return MenuCategoryApiService(ref.watch(deliveryApiClientProvider));
});

/// REST client for `/api/v1/MenuCategories/*`.
class MenuCategoryApiService {
  final Dio _dio;

  MenuCategoryApiService(this._dio);

  /// Get menu categories for a specific shop.
  Future<List<MenuCategoryDto>> getByShop(String shopId) async {
    try {
      final response = await _dio.get('MenuCategories/shop/$shopId');
      final parsed = parseApiListResponse(
        response.data,
        MenuCategoryDto.fromJson,
      );
      ensureSuccess(parsed);
      return parsed.value ?? [];
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  /// Create a new menu category.
  Future<MenuCategoryDto> create(Map<String, dynamic> data) async {
    try {
      final response = await _dio.post('MenuCategories', data: data);
      final parsed = parseApiResponse(response.data, MenuCategoryDto.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }
}
