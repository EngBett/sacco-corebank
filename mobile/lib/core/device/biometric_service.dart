import 'package:local_auth/local_auth.dart';

/// Device-local biometric checks — never a server-side factor. Used to re-open the app on a
/// returning, already-trusted device (the check just unlocks the stored refresh token; the server
/// never learns it happened, ADR 0008) and, when the member opts in, to show hidden balances.
class BiometricService {
  BiometricService() : _auth = LocalAuthentication();

  final LocalAuthentication _auth;

  Future<bool> isAvailable() async {
    try {
      final supported = await _auth.isDeviceSupported();
      final canCheck = await _auth.canCheckBiometrics;
      return supported && canCheck;
    } catch (_) {
      return false;
    }
  }

  /// True when at least one fingerprint/face is enrolled — the device merely supporting a sensor isn't enough
  /// to offer "use fingerprint" as an unlock method.
  Future<bool> hasEnrolledBiometrics() async {
    try {
      return await isAvailable() && (await _auth.getAvailableBiometrics()).isNotEmpty;
    } catch (_) {
      return false;
    }
  }

  /// What to call the enrolled biometric in copy: iPhones with Face ID say so, everything else is a fingerprint.
  Future<String> label() async {
    try {
      final types = await _auth.getAvailableBiometrics();
      return types.contains(BiometricType.face) && !types.contains(BiometricType.fingerprint)
          ? 'Face ID'
          : 'fingerprint';
    } catch (_) {
      return 'fingerprint';
    }
  }

  /// [biometricOnly] refuses the phone's own screen-lock PIN/pattern as a fallback, for gates where the app offers
  /// its own fallback (the SACCO PIN) instead.
  Future<bool> authenticate({String reason = 'Unlock your SACCO account', bool biometricOnly = false}) async {
    try {
      return await _auth.authenticate(
        localizedReason: reason,
        options: AuthenticationOptions(biometricOnly: biometricOnly, stickyAuth: true),
      );
    } catch (_) {
      return false;
    }
  }
}
