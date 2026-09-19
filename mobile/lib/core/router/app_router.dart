import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/accounts/presentation/pages/statement_page.dart';
import '../../features/auth/presentation/pages/biometric_gate_page.dart';
import '../../features/auth/presentation/pages/login_page.dart';
import '../../features/auth/presentation/pages/otp_page.dart';
import '../../features/auth/presentation/providers/auth_controller.dart';
import '../../features/auth/presentation/providers/auth_state.dart';
import '../../features/dividends/presentation/pages/dividends_page.dart';
import '../../features/marketplace/presentation/pages/share_marketplace_page.dart';
import '../../features/payments/presentation/pages/deposit_page.dart';
import '../../features/payments/presentation/pages/payments_page.dart';
import '../../features/payments/presentation/pages/withdraw_page.dart';
import '../../features/accounts/presentation/pages/dashboard_page.dart';
import '../../features/accounts/presentation/pages/profile_page.dart';
import '../widgets/home_shell.dart';
import '../widgets/splash_page.dart';

/// Routes reachable without an authenticated session — the ones the pre-auth redirect must not
/// bounce away from while their own flow (entering a PIN, verifying OTP) is in progress.
const _preAuthRoutes = {'/login', '/otp'};

final routerProvider = Provider<GoRouter>((ref) {
  final authState = ref.watch(authControllerProvider);

  return GoRouter(
    initialLocation: '/splash',
    redirect: (context, state) {
      final at = state.matchedLocation;
      switch (authState.status) {
        case AuthStatus.unknown:
          return at == '/splash' ? null : '/splash';
        case AuthStatus.awaitingBiometric:
          return at == '/biometric' ? null : '/biometric';
        case AuthStatus.authenticated:
          return (_preAuthRoutes.contains(at) || at == '/splash' || at == '/biometric') ? '/dashboard' : null;
        case AuthStatus.unauthenticated:
          return _preAuthRoutes.contains(at) ? null : '/login';
      }
    },
    routes: [
      GoRoute(path: '/splash', builder: (context, state) => const SplashPage()),
      GoRoute(path: '/biometric', builder: (context, state) => const BiometricGatePage()),
      GoRoute(path: '/login', builder: (context, state) => const LoginPage()),
      GoRoute(
        path: '/otp',
        builder: (context, state) {
          final extra = state.extra as Map<String, Object?>;
          return OtpPage(
            phone: extra['phone'] as String,
            pin: extra['pin'] as String,
            expiresInSeconds: extra['expiresInSeconds'] as int,
          );
        },
      ),
      ShellRoute(
        builder: (context, state, child) => HomeShell(child: child),
        routes: [
          GoRoute(path: '/dashboard', builder: (context, state) => const DashboardPage()),
          GoRoute(path: '/payments', builder: (context, state) => const PaymentsPage()),
          GoRoute(path: '/dividends', builder: (context, state) => const DividendsPage()),
          GoRoute(path: '/profile', builder: (context, state) => const ProfilePage()),
        ],
      ),
      GoRoute(
        path: '/statement/:accountNumber',
        builder: (context, state) => StatementPage(accountNumber: state.pathParameters['accountNumber']!),
      ),
      GoRoute(path: '/deposit', builder: (context, state) => const DepositPage()),
      GoRoute(path: '/withdraw', builder: (context, state) => const WithdrawPage()),
      GoRoute(path: '/shares-marketplace', builder: (context, state) => const ShareMarketplacePage()),
    ],
  );
});
