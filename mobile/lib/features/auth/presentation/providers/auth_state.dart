import '../../domain/entities/auth_session.dart';

enum AuthStatus {
  /// Checking secure storage / deciding whether to prompt biometrics on cold start.
  unknown,

  /// No usable stored session — show phone + PIN entry.
  unauthenticated,

  /// A stored session exists and biometrics are available on this device — prompt before restoring.
  awaitingBiometric,

  authenticated,
}

/// Router-relevant auth state only. Loading/error feedback for the login and OTP steps lives in
/// the pages themselves (same local-state pattern as deposit/withdraw), not here — see
/// AuthController's request/sign-in methods, which throw rather than mutate this state on failure.
class AuthState {
  const AuthState._(this.status, {this.session});

  const AuthState.unknown() : this._(AuthStatus.unknown);
  const AuthState.unauthenticated() : this._(AuthStatus.unauthenticated);
  const AuthState.awaitingBiometric() : this._(AuthStatus.awaitingBiometric);
  const AuthState.authenticated(AuthSession session) : this._(AuthStatus.authenticated, session: session);

  final AuthStatus status;
  final AuthSession? session;

  bool get isAuthenticated => status == AuthStatus.authenticated && session != null;
}
