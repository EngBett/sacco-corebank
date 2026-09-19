import 'package:dio/dio.dart';

import '../../features/auth/domain/repositories/auth_repository.dart';
import '../config/app_config.dart';
import '../storage/secure_storage.dart';

/// Attaches the bearer token and tenant header to every request, and retries
/// once through a refresh on a 401 — mirrors the portal BFF's session refresh,
/// just done on-device instead of server-side.
class AuthInterceptor extends Interceptor {
  AuthInterceptor(this._storage, this._authRepository);

  final SecureStorage _storage;
  final AuthRepository _authRepository;
  final Dio _retryDio = Dio(BaseOptions(baseUrl: AppConfig.apiBaseUrl));

  @override
  Future<void> onRequest(RequestOptions options, RequestInterceptorHandler handler) async {
    final token = await _storage.readAccessToken();
    if (token != null) {
      options.headers['Authorization'] = 'Bearer $token';
    }
    options.headers['X-Tenant'] = AppConfig.tenantSlug;
    handler.next(options);
  }

  @override
  Future<void> onError(DioException err, ErrorInterceptorHandler handler) async {
    final response = err.response;
    if (response?.statusCode != 401 || err.requestOptions.extra['retried'] == true) {
      handler.next(err);
      return;
    }

    final session = await _authRepository.refresh();
    if (session == null) {
      handler.next(err);
      return;
    }

    final retriedRequest = err.requestOptions
      ..headers['Authorization'] = 'Bearer ${session.accessToken}'
      ..extra['retried'] = true;

    try {
      final response = await _retryDio.fetch(retriedRequest);
      handler.resolve(response);
    } on DioException catch (retryError) {
      handler.next(retryError);
    }
  }
}
