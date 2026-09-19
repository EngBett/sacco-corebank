import 'package:dio/dio.dart';

import '../../../../core/network/api_paths.dart';

class PaymentsRemoteDataSource {
  PaymentsRemoteDataSource(this._dio);
  final Dio _dio;

  Future<Map<String, dynamic>> topUp({
    required String provider,
    required double amount,
    required String accountNumber,
    String? loanNumber,
  }) async {
    final response = await _dio.post(
      ApiPaths.paymentsTopUp,
      data: {'provider': provider, 'amount': amount, 'accountNumber': accountNumber, 'loanNumber': loanNumber},
    );
    return response.data as Map<String, dynamic>;
  }

  Future<List<Map<String, dynamic>>> getMyPayments() async {
    final response = await _dio.get(ApiPaths.payments);
    return (response.data as List<dynamic>).cast<Map<String, dynamic>>();
  }

  Future<Map<String, dynamic>> requestWithdrawal({
    required String accountNumber,
    required double amount,
    required String channelWireName,
    String? destination,
    String? narrative,
  }) async {
    final response = await _dio.post(
      ApiPaths.withdrawals,
      data: {
        'accountNumber': accountNumber,
        'amount': amount,
        'channel': channelWireName,
        'destination': destination,
        'narrative': narrative,
      },
    );
    return response.data as Map<String, dynamic>;
  }

  Future<List<Map<String, dynamic>>> getMyWithdrawals() async {
    final response = await _dio.get(ApiPaths.withdrawals);
    return (response.data as List<dynamic>).cast<Map<String, dynamic>>();
  }
}
