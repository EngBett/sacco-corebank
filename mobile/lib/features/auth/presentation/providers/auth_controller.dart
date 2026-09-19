import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/device/biometric_service.dart';
import '../../../../core/di/service_locator.dart';
import '../../domain/entities/auth_session.dart';
import '../../domain/repositories/auth_repository.dart';
import 'auth_state.dart';

final authRepositoryProvider = Provider<AuthRepository>((ref) => sl<AuthRepository>());
final biometricServiceProvider = Provider<BiometricService>((ref) => sl<BiometricService>());

final authControllerProvider = StateNotifierProvider<AuthController, AuthState>(
  (ref) => AuthController(ref.watch(authRepositoryProvider), ref.watch(biometricServiceProvider)),
);

class AuthController extends StateNotifier<AuthState> {
  AuthController(this._repository, this._biometrics) : super(const AuthState.unknown()) {
    bootstrap();
  }

  final AuthRepository _repository;
  final BiometricService _biometrics;

  Future<void> bootstrap() async {
    if (!await _repository.hasStoredSession()) {
      state = const AuthState.unauthenticated();
      return;
    }
    state = await _biometrics.isAvailable() ? const AuthState.awaitingBiometric() : const AuthState.unauthenticated();
  }

  /// Called from the biometric-prompt screen. Returns false (state unchanged) on a cancelled or
  /// failed biometric check, so the screen can offer a retry without losing its place.
  Future<bool> unlockWithBiometrics() async {
    if (!await _biometrics.authenticate()) return false;
    final session = await _repository.restoreSession();
    state = session != null ? AuthState.authenticated(session) : const AuthState.unauthenticated();
    return session != null;
  }

  /// Bails out of the biometric prompt into normal phone+PIN entry — the trusted-device fast path
  /// still skips OTP, so this is a fallback, not a full re-verification.
  void useCredentialsInstead() => state = const AuthState.unauthenticated();

  Future<OtpRequestOutcome> requestOtp(String phone, String pin) =>
      _repository.requestOtpIfNeeded(phone: phone, pin: pin);

  Future<AuthSession> signIn({required String phone, required String pin, String? otp}) async {
    final session = await _repository.signIn(phone: phone, pin: pin, otp: otp);
    state = AuthState.authenticated(session);
    return session;
  }

  Future<String?> lastUsedPhone() => _repository.lastUsedPhone();

  Future<void> logout() async {
    await _repository.logout();
    state = const AuthState.unauthenticated();
  }
}
