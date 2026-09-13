import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../models/shop.dart';
import '../../../models/store_report.dart';
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

  /// Get sales and order summary report.
  Future<StoreReportSummaryDto> getReportSummary(String shopId, {String period = 'day'}) async {
    try {
      final response = await _dio.get(
        'shops/$shopId/reports/summary',
        queryParameters: {'period': period},
      );
      final parsed = parseApiResponse(response.data, StoreReportSummaryDto.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  /// Download sales report CSV content.
  Future<String> downloadReportExportCsv(String shopId, {String period = 'day'}) async {
    try {
      final response = await _dio.get(
        'shops/$shopId/reports/export',
        queryParameters: {'period': period, 'format': 'csv'},
        options: Options(responseType: ResponseType.plain),
      );
      return response.data?.toString() ?? '';
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }
}

