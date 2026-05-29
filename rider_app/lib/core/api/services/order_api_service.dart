import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../models/order.dart';
import '../../config/app_constants.dart';
import '../api_helpers.dart';
import '../delivery_api_client.dart';

final orderApiServiceProvider = Provider<OrderApiService>((ref) {
  return OrderApiService(ref.watch(deliveryApiClientProvider));
});

/// REST client for rider order operations.
class OrderApiService {
  final Dio _dio;

  OrderApiService(this._dio);

  Future<List<OrderDto>> getMyOrders() async {
    try {
      final response = await _dio.get('${AppConstants.ordersEndpoint}/my');
      final parsed = parseApiListResponse(response.data, OrderDto.fromJson);
      ensureSuccess(parsed);
      return parsed.value ?? [];
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  Future<List<OrderDto>> getCustomerOrders() async {
    try {
      final response = await _dio.get('${AppConstants.ordersEndpoint}/customer');
      final parsed = parseApiListResponse(response.data, OrderDto.fromJson);
      ensureSuccess(parsed);
      return parsed.value ?? [];
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  Future<OrderDto> getById(String id) async {
    try {
      final response = await _dio.get('${AppConstants.ordersEndpoint}/$id');
      final parsed = parseApiResponse(response.data, OrderDto.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  Future<OrderDto> updateStatus({
    required String orderId,
    required String status,
  }) async {
    try {
      final response = await _dio.patch(
        '${AppConstants.ordersEndpoint}/$orderId/status',
        data: {'Status': status},
      );
      final parsed = parseApiResponse(response.data, OrderDto.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  Future<OrderDto> createOrder(CreateOrderDto dto) async {
    try {
      final response = await _dio.post(
        AppConstants.ordersEndpoint,
        data: dto.toJson(),
      );
      final parsed = parseApiResponse(response.data, OrderDto.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }
}
