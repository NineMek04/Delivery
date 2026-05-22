import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../models/rider.dart';
import '../../config/app_constants.dart';
import '../api_helpers.dart';
import '../delivery_api_client.dart';

final riderApiServiceProvider = Provider<RiderApiService>((ref) {
  return RiderApiService(ref.watch(deliveryApiClientProvider));
});

/// REST client for rider profile.
class RiderApiService {
  final Dio _dio;

  RiderApiService(this._dio);

  Future<RiderDto> getById(String idOrRef) async {
    try {
      final response = await _dio.get('${AppConstants.ridersEndpoint}/$idOrRef');
      final parsed = parseApiResponse(response.data, RiderDto.fromJson);
      ensureSuccess(parsed);
      return parsed.value!;
    } on DioException catch (e) {
      throw wrapDioError(e).error ?? e;
    }
  }
}
