import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../models/customer_address.dart';
import '../api_helpers.dart';
import '../delivery_api_client.dart';
import '../models/api_response.dart';

final customerAddressApiServiceProvider = Provider<CustomerAddressApiService>((ref) {
  return CustomerAddressApiService(ref.watch(deliveryApiClientProvider));
});

class CustomerAddressApiService {
  final Dio _dio;

  CustomerAddressApiService(this._dio);

  Future<PaginatedResult<CustomerAddressDto>> getAddresses({
    int page = 1,
    int pageSize = 50,
  }) async {
    try {
      final response = await _dio.get(
        'customeraddresses',
        queryParameters: {
          'page': page,
          'pageSize': pageSize,
        },
      );
      final parsed = parseApiResponse(
        response.data,
        (json) => PaginatedResult.fromJson(json, CustomerAddressDto.fromJson),
      );
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  Future<CustomerAddressDto> createAddress({
    required String name,
    required String addressLine1,
    String? addressLine2,
    required String city,
    required String state,
    required String postalCode,
    required double latitude,
    required double longitude,
    required bool isDefault,
  }) async {
    try {
      final response = await _dio.post(
        'customeraddresses',
        data: {
          'Name': name,
          'AddressLine1': addressLine1,
          if (addressLine2 != null) 'AddressLine2': addressLine2,
          'City': city,
          'State': state,
          'PostalCode': postalCode,
          'Latitude': latitude,
          'Longitude': longitude,
          'IsDefault': isDefault,
        },
      );
      final parsed = parseApiResponse(response.data, CustomerAddressDto.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  Future<CustomerAddressDto> updateAddress(
    String id, {
    String? name,
    String? addressLine1,
    String? addressLine2,
    String? city,
    String? state,
    String? postalCode,
    double? latitude,
    double? longitude,
    bool? isDefault,
  }) async {
    try {
      final response = await _dio.put(
        'customeraddresses/$id',
        data: {
          if (name != null) 'Name': name,
          if (addressLine1 != null) 'AddressLine1': addressLine1,
          if (addressLine2 != null) 'AddressLine2': addressLine2,
          if (city != null) 'City': city,
          if (state != null) 'State': state,
          if (postalCode != null) 'PostalCode': postalCode,
          if (latitude != null) 'Latitude': latitude,
          if (longitude != null) 'Longitude': longitude,
          if (isDefault != null) 'IsDefault': isDefault,
        },
      );
      final parsed = parseApiResponse(response.data, CustomerAddressDto.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }

  Future<void> deleteAddress(String id) async {
    try {
      final response = await _dio.delete('customeraddresses/$id');
      final mapData = asMap(response.data);
      final parsed = ApiResponse.fromJson(mapData);
      if (!parsed.success) {
        throw ApiException(parsed.message ?? 'ลบที่อยู่ไม่สำเร็จ');
      }
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }
}
