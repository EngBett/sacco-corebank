import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Thin wrapper around Keychain/Keystore for the ROPC tokens, this device's
/// stable id, and the phone number to pre-fill on a returning sign-in. Never
/// cache financial data here (ADR 0008: online-only, no local persistence of
/// financial data beyond a short-lived in-memory cache).
class SecureStorage {
  SecureStorage()
    : _storage = const FlutterSecureStorage(
        aOptions: AndroidOptions(encryptedSharedPreferences: true),
        iOptions: IOSOptions(accessibility: KeychainAccessibility.first_unlock),
      );

  final FlutterSecureStorage _storage;

  static const _accessTokenKey = 'sacco.access_token';
  static const _refreshTokenKey = 'sacco.refresh_token';
  static const _accessTokenExpiryKey = 'sacco.access_token_expiry';
  static const _deviceIdKey = 'sacco.device_id';
  static const _lastPhoneKey = 'sacco.last_phone';
  static const _requirePinForBalancesKey = 'sacco.require_pin_for_balances';
  static const _biometricsForBalancesKey = 'sacco.biometrics_for_balances';

  Future<void> saveTokens({required String accessToken, String? refreshToken, DateTime? accessTokenExpiry}) async {
    await _storage.write(key: _accessTokenKey, value: accessToken);
    if (refreshToken != null) {
      await _storage.write(key: _refreshTokenKey, value: refreshToken);
    }
    if (accessTokenExpiry != null) {
      await _storage.write(key: _accessTokenExpiryKey, value: accessTokenExpiry.toIso8601String());
    }
  }

  Future<String?> readAccessToken() => _storage.read(key: _accessTokenKey);
  Future<String?> readRefreshToken() => _storage.read(key: _refreshTokenKey);

  Future<DateTime?> readAccessTokenExpiry() async {
    final raw = await _storage.read(key: _accessTokenExpiryKey);
    return raw == null ? null : DateTime.tryParse(raw);
  }

  /// Clears only the session (tokens). The device id and last-used phone survive a sign-out,
  /// since the device stays "trusted" server-side until the member revokes it.
  Future<void> clearSession() async {
    await _storage.delete(key: _accessTokenKey);
    await _storage.delete(key: _refreshTokenKey);
    await _storage.delete(key: _accessTokenExpiryKey);
  }

  Future<String?> readDeviceId() => _storage.read(key: _deviceIdKey);
  Future<void> saveDeviceId(String deviceId) => _storage.write(key: _deviceIdKey, value: deviceId);

  Future<String?> readLastPhone() => _storage.read(key: _lastPhoneKey);
  Future<void> saveLastPhone(String phone) => _storage.write(key: _lastPhoneKey, value: phone);

  /// A device privacy preference, not financial data: hide balances until the member re-enters their PIN.
  Future<bool> readRequirePinForBalances() async => await _storage.read(key: _requirePinForBalancesKey) == 'true';
  Future<void> saveRequirePinForBalances(bool value) => _storage.write(key: _requirePinForBalancesKey, value: '$value');

  /// Whether a fingerprint (or Face ID) may stand in for the PIN when showing hidden balances.
  Future<bool> readBiometricsForBalances() async => await _storage.read(key: _biometricsForBalancesKey) == 'true';
  Future<void> saveBiometricsForBalances(bool value) => _storage.write(key: _biometricsForBalancesKey, value: '$value');
}
