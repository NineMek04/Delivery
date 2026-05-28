import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../models/shop.dart';
import '../api_helpers.dart';
import '../delivery_api_client.dart';

final shopApiServiceProvider = Provider<ShopApiService>((ref) {
  return ShopApiService(ref.watch(deliveryApiClientProvider));
});

/// REST client for `/api/v1/shops/*`.
class ShopApiService {
  final Dio _dio;

  ShopApiService(this._dio);

  /// Get shop by ID.
  Future<ShopDto> getById(String shopId) async {
    try {
      final response = await _dio.get('shops/$shopId');
      final parsed = parseApiResponse(response.data, ShopDto.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  /// Update shop details.
  Future<ShopDto> update(String shopId, Map<String, dynamic> data) async {
    try {
      final response = await _dio.put('shops/$shopId', data: data);
      final parsed = parseApiResponse(response.data, ShopDto.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }
}
