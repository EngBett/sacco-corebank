import 'package:dio/dio.dart';

import '../../../../core/network/api_paths.dart';

class ShareMarketplaceRemoteDataSource {
  ShareMarketplaceRemoteDataSource(this._dio);
  final Dio _dio;

  Future<Map<String, dynamic>> listSharesForSale(double amount) async {
    final response = await _dio.post(ApiPaths.shareListings, data: {'amount': amount});
    return response.data as Map<String, dynamic>;
  }

  Future<List<Map<String, dynamic>>> getOpenListings() async {
    final response = await _dio.get(ApiPaths.openShareListings);
    return (response.data as List<dynamic>).cast<Map<String, dynamic>>();
  }

  Future<List<Map<String, dynamic>>> getMyListings() async {
    final response = await _dio.get(ApiPaths.myShareListings);
    return (response.data as List<dynamic>).cast<Map<String, dynamic>>();
  }

  Future<Map<String, dynamic>> claim(String listingId) async {
    final response = await _dio.post(ApiPaths.claimShareListing(listingId));
    return response.data as Map<String, dynamic>;
  }

  Future<Map<String, dynamic>> cancel(String listingId) async {
    final response = await _dio.post(ApiPaths.cancelShareListing(listingId));
    return response.data as Map<String, dynamic>;
  }
}
