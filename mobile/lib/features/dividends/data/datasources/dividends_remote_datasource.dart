import 'package:dio/dio.dart';

import '../../../../core/network/api_paths.dart';

class DividendsRemoteDataSource {
  DividendsRemoteDataSource(this._dio);
  final Dio _dio;

  Future<List<Map<String, dynamic>>> getMyDividends({int? year}) async {
    final response = await _dio.get(ApiPaths.dividends, queryParameters: year == null ? null : {'year': year});
    return (response.data as List<dynamic>).cast<Map<String, dynamic>>();
  }
}
