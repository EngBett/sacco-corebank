import 'package:dio/dio.dart';

import '../../features/auth/domain/repositories/auth_repository.dart';
import '../config/app_config.dart';
import '../storage/secure_storage.dart';
import 'auth_interceptor.dart';

/// Talks to `/api/self/*` on the .NET backend (ADR 0008). One Dio instance,
/// shared across every feature's data source.
class ApiClient {
  ApiClient(SecureStorage storage, AuthRepository authRepository)
    : dio = Dio(
        BaseOptions(
          baseUrl: AppConfig.apiBaseUrl,
          connectTimeout: const Duration(seconds: 15),
          receiveTimeout: const Duration(seconds: 15),
          headers: {'Content-Type': 'application/json'},
        ),
      ) {
    dio.interceptors.add(AuthInterceptor(storage, authRepository));
  }

  final Dio dio;
}
