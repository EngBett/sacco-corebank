/// Wrong phone number or PIN — server never distinguishes "no such login" from "wrong PIN".
class InvalidCredentialsException implements Exception {
  const InvalidCredentialsException();
}

/// The `otp` field was missing or didn't match the pending challenge for this device.
class InvalidOtpException implements Exception {
  const InvalidOtpException();
}

/// Any other network/server failure — shown as a generic "try again" message.
class AuthUnavailableException implements Exception {
  const AuthUnavailableException([this.message]);
  final String? message;
}
