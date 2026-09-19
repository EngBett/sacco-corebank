/// Central place for everything environment-specific: API base URL and the OIDC
/// "mobile" client wiring (ADR 0008, 2026-09-16 native-login update). Sign-in is
/// native phone+PIN via the Resource Owner Password grant at `/connect/token`
/// (`client_id=mobile`) — there is no browser/redirect hop, so no redirect URI.
class AppConfig {
  const AppConfig._();

  /// Overridable at build/run time: `--dart-define=API_BASE_URL=http://10.0.2.2:5000`
  /// (Android emulator can't resolve `localhost` as the host machine).
  static const String apiBaseUrl = String.fromEnvironment('API_BASE_URL', defaultValue: 'http://10.0.2.2:5000');

  static const String tenantSlug = String.fromEnvironment('TENANT_SLUG', defaultValue: 'demo');

  static const String oidcClientId = 'mobile';
  static const String oidcTokenEndpoint = '$apiBaseUrl/connect/token';
  static const String oidcScope = 'openid profile tenant sacco-api offline_access';
}
