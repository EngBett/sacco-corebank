import 'package:dio/dio.dart';

import '../../../../core/network/api_paths.dart';

class AccountsRemoteDataSource {
  AccountsRemoteDataSource(this._dio);
  final Dio _dio;

  Future<List<Map<String, dynamic>>> getAccounts() async {
    final response = await _dio.get(ApiPaths.accounts);
    return (response.data as List<dynamic>).cast<Map<String, dynamic>>();
  }

  Future<Map<String, dynamic>> getSummary() async {
    final response = await _dio.get(ApiPaths.summary);
    return response.data as Map<String, dynamic>;
  }

  Future<Map<String, dynamic>> getProfile() async {
    final response = await _dio.get(ApiPaths.profile);
    return response.data as Map<String, dynamic>;
  }

  Future<Map<String, dynamic>> getStatement(String accountNumber, {DateTime? from, DateTime? to}) async {
    final response = await _dio.get(
      ApiPaths.statement(accountNumber),
      queryParameters: {if (from != null) 'from': _dateOnly(from), if (to != null) 'to': _dateOnly(to)},
    );
    return response.data as Map<String, dynamic>;
  }

  Future<Map<String, dynamic>> revealBalance(String accountNumber, String idempotencyKey) async {
    final response = await _dio.post(ApiPaths.balanceEnquiry(accountNumber), data: {'idempotencyKey': idempotencyKey});
    return response.data as Map<String, dynamic>;
  }

  Future<Map<String, dynamic>> quoteFee({
    required String transactionType,
    required String accountNumber,
    required String channel,
    required double amount,
  }) async {
    final response = await _dio.get(
      ApiPaths.feeQuote,
      queryParameters: {
        'transactionType': transactionType,
        'accountNumber': accountNumber,
        'channel': channel,
        'amount': amount,
      },
    );
    return response.data as Map<String, dynamic>;
  }

  Future<Map<String, dynamic>> verifyPin(String pin) async {
    final response = await _dio.post(ApiPaths.verifyPin, data: {'pin': pin});
    return response.data as Map<String, dynamic>;
  }

  String _dateOnly(DateTime d) =>
      '${d.year.toString().padLeft(4, '0')}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';
}
