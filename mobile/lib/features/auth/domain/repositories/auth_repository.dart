import '../entities/auth_session.dart';

/// Result of asking the server whether this device needs to prove phone
/// possession before it can sign in (ADR 0008: native login redesign).
class OtpRequestOutcome {
  const OtpRequestOutcome({required this.otpRequired, required this.expiresInSeconds});
  final bool otpRequired;
  final int expiresInSeconds;
}

abstract class AuthRepository {
  /// Step 1: checks phone + PIN and returns whether this device also needs an SMS code.
  /// Throws [InvalidCredentialsException] on a wrong phone/PIN — no SMS is ever sent for that.
  Future<OtpRequestOutcome> requestOtpIfNeeded({required String phone, required String pin});

  /// Step 2: completes sign-in via `/connect/token` (ROPC, `client_id=mobile`). [otp] is required
  /// only when the matching [requestOtpIfNeeded] call said so; omit it for an already-trusted device.
  /// Throws [InvalidCredentialsException] or [InvalidOtpException].
  Future<AuthSession> signIn({required String phone, required String pin, String? otp});

  /// Silent renewal using the stored refresh token. Returns null (and clears the stored session)
  /// if the refresh token is missing, expired, or revoked.
  Future<AuthSession?> refresh();

  /// A still-valid session from storage, refreshing it first if the access token expired.
  Future<AuthSession?> restoreSession();

  /// Cheap check for whether there's anything worth restoring, without refreshing — used to decide
  /// whether to prompt biometrics on cold start.
  Future<bool> hasStoredSession();

  /// The phone number used on this device's last successful sign-in, to pre-fill the field.
  Future<String?> lastUsedPhone();

  Future<void> logout();
}
