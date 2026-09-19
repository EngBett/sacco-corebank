import 'package:dio/dio.dart';

import '../../../../core/config/app_config.dart';
import '../../../../core/device/device_id_service.dart';
import '../../../../core/network/api_paths.dart';
import '../../../../core/storage/secure_storage.dart';
import '../../domain/entities/auth_exceptions.dart';
import '../../domain/entities/auth_session.dart';
import '../../domain/repositories/auth_repository.dart';

class AuthRepositoryImpl implements AuthRepository {
  AuthRepositoryImpl(this._storage, this._deviceId)
    : _dio = Dio(
        BaseOptions(
          baseUrl: AppConfig.apiBaseUrl,
          connectTimeout: const Duration(seconds: 15),
          receiveTimeout: const Duration(seconds: 15),
          headers: {'X-Tenant': AppConfig.tenantSlug},
        ),
      );

  final SecureStorage _storage;
  final DeviceIdService _deviceId;
  final Dio _dio;

  @override
  Future<OtpRequestOutcome> requestOtpIfNeeded({required String phone, required String pin}) async {
    final deviceId = await _deviceId.getOrCreate();
    try {
      final response = await _dio.post(
        ApiPaths.requestLoginOtp,
        data: {'phone': phone, 'pin': pin, 'deviceId': deviceId},
      );
      final body = response.data as Map<String, dynamic>;
      return OtpRequestOutcome(
        otpRequired: body['otpRequired'] as bool,
        expiresInSeconds: body['expiresInSeconds'] as int,
      );
    } on DioException catch (e) {
      if (e.response?.statusCode == 401) throw const InvalidCredentialsException();
      throw AuthUnavailableException(e.message);
    }
  }

  @override
  Future<AuthSession> signIn({required String phone, required String pin, String? otp}) async {
    final deviceId = await _deviceId.getOrCreate();
    final session = await _tokenRequest({
      'grant_type': 'password',
      'client_id': AppConfig.oidcClientId,
      'tenant': AppConfig.tenantSlug,
      'username': phone,
      'password': pin,
      'scope': AppConfig.oidcScope,
      'device_id': deviceId,
      if (otp != null) 'otp': otp,
    });
    await _storage.saveLastPhone(phone);
    return session;
  }

  @override
  Future<AuthSession?> refresh() async {
    final refreshToken = await _storage.readRefreshToken();
    if (refreshToken == null) return null;
    try {
      final deviceId = await _deviceId.getOrCreate();
      return await _tokenRequest({
        'grant_type': 'refresh_token',
        'client_id': AppConfig.oidcClientId,
        'refresh_token': refreshToken,
        'device_id': deviceId,
      });
    } catch (_) {
      await _storage.clearSession();
      return null;
    }
  }

  @override
  Future<AuthSession?> restoreSession() async {
    final accessToken = await _storage.readAccessToken();
    final expiresAt = await _storage.readAccessTokenExpiry();
    if (accessToken == null || expiresAt == null) return null;

    if (DateTime.now().isBefore(expiresAt.subtract(const Duration(seconds: 30)))) {
      final refreshToken = await _storage.readRefreshToken();
      return AuthSession(accessToken: accessToken, expiresAt: expiresAt, refreshToken: refreshToken);
    }
    return refresh();
  }

  @override
  Future<bool> hasStoredSession() async => (await _storage.readRefreshToken()) != null;

  @override
  Future<String?> lastUsedPhone() => _storage.readLastPhone();

  @override
  Future<void> logout() => _storage.clearSession();

  Future<AuthSession> _tokenRequest(Map<String, String> form) async {
    try {
      final response = await _dio.post(
        AppConfig.oidcTokenEndpoint,
        data: form,
        options: Options(contentType: Headers.formUrlEncodedContentType),
      );
      final body = response.data as Map<String, dynamic>;
      final accessToken = body['access_token'] as String;
      final expiresIn = body['expires_in'] as int;
      final refreshToken = body['refresh_token'] as String?;
      final expiresAt = DateTime.now().add(Duration(seconds: expiresIn));

      await _storage.saveTokens(accessToken: accessToken, refreshToken: refreshToken, accessTokenExpiry: expiresAt);
      return AuthSession(accessToken: accessToken, expiresAt: expiresAt, refreshToken: refreshToken);
    } on DioException catch (e) {
      final description = (e.response?.data is Map) ? (e.response!.data as Map)['error_description'] as String? : null;
      if (description == 'otp_required_or_invalid') throw const InvalidOtpException();
      if (e.response?.statusCode == 400 || e.response?.statusCode == 401) throw const InvalidCredentialsException();
      throw AuthUnavailableException(e.message);
    }
  }
}
