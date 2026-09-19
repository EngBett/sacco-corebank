import 'package:dio/dio.dart';

import '../../../../core/network/api_paths.dart';

class BrandingRemoteDataSource {
  BrandingRemoteDataSource(this._dio);
  final Dio _dio;

  Future<Map<String, dynamic>> getBranding() async {
    final response = await _dio.get(ApiPaths.tenantBranding);
    final json = Map<String, dynamic>.of(response.data as Map<String, dynamic>);
    // A tenant logo may be root-relative (/tenant-assets/demo/logo.png, served by the API itself); make it absolute
    // against the API address this build talks to. Absolute URLs pass through unchanged.
    final logo = json['logoUrl'] as String?;
    if (logo != null && logo.isNotEmpty) json['logoUrl'] = Uri.parse(_dio.options.baseUrl).resolve(logo).toString();
    return json;
  }
}
