class AuthSession {
  const AuthSession({required this.accessToken, required this.expiresAt, this.refreshToken});

  final String accessToken;
  final DateTime expiresAt;
  final String? refreshToken;

  bool get isExpired => DateTime.now().isAfter(expiresAt);
}
