import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:sacco_member/core/device/biometric_service.dart';
import 'package:sacco_member/features/auth/domain/entities/auth_exceptions.dart';
import 'package:sacco_member/features/auth/domain/entities/auth_session.dart';
import 'package:sacco_member/features/auth/domain/repositories/auth_repository.dart';
import 'package:sacco_member/features/auth/presentation/pages/login_page.dart';
import 'package:sacco_member/features/auth/presentation/pages/otp_page.dart';
import 'package:sacco_member/features/auth/presentation/providers/auth_controller.dart';
import 'package:sacco_member/features/branding/presentation/providers/branding_providers.dart';

class _FakeAuthRepository implements AuthRepository {
  _FakeAuthRepository({this.otpRequired = false, this.throwOnRequest, this.throwOnSignIn});

  final bool otpRequired;
  final Object? throwOnRequest;
  final Object? throwOnSignIn;
  int signInCalls = 0;

  @override
  Future<bool> hasStoredSession() async => false;

  @override
  Future<String?> lastUsedPhone() async => null;

  @override
  Future<OtpRequestOutcome> requestOtpIfNeeded({required String phone, required String pin}) async {
    if (throwOnRequest != null) throw throwOnRequest!;
    return OtpRequestOutcome(otpRequired: otpRequired, expiresInSeconds: 300);
  }

  @override
  Future<AuthSession> signIn({required String phone, required String pin, String? otp}) async {
    signInCalls++;
    if (throwOnSignIn != null) throw throwOnSignIn!;
    return AuthSession(accessToken: 'token', expiresAt: DateTime.now().add(const Duration(minutes: 15)));
  }

  @override
  Future<AuthSession?> refresh() async => null;

  @override
  Future<AuthSession?> restoreSession() async => null;

  @override
  Future<void> logout() async {}
}

Widget _wrap(AuthRepository repository, {Widget home = const LoginPage()}) => ProviderScope(
  overrides: [
    authRepositoryProvider.overrideWithValue(repository),
    biometricServiceProvider.overrideWithValue(BiometricService()),
    tenantBrandingProvider.overrideWith((ref) async => throw Exception('offline in test')),
  ],
  child: MaterialApp(home: home),
);

/// A phone-sized screen (Pixel 5, 393×851dp): the default 800×600 test surface puts the Sign in button below the fold.
void _usePhoneScreen(WidgetTester tester) {
  tester.view.physicalSize = const Size(1080, 2340);
  tester.view.devicePixelRatio = 2.75;
  addTearDown(tester.view.reset);
}

void main() {
  testWidgets('an already-trusted device signs in directly, with no OTP step', (tester) async {
    final repository = _FakeAuthRepository(otpRequired: false);
    _usePhoneScreen(tester);
    await tester.pumpWidget(_wrap(repository));
    await tester.pump();

    await tester.enterText(find.widgetWithText(TextFormField, 'Phone number'), '254712345678');
    await tester.enterText(find.widgetWithText(TextFormField, 'PIN'), '2468');
    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pump();
    await tester.pump();

    expect(repository.signInCalls, 1);
    await tester.pump(const Duration(seconds: 1)); // let the staggered entrance delays run out
    await tester.pumpWidget(const SizedBox.shrink());
  });

  testWidgets('shows an error message on invalid credentials', (tester) async {
    final repository = _FakeAuthRepository(throwOnRequest: const InvalidCredentialsException());
    _usePhoneScreen(tester);
    await tester.pumpWidget(_wrap(repository));
    await tester.pump();

    await tester.enterText(find.widgetWithText(TextFormField, 'Phone number'), '254712345678');
    await tester.enterText(find.widgetWithText(TextFormField, 'PIN'), '0000');
    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pump();
    await tester.pump();

    expect(find.text('Incorrect phone number or PIN.'), findsOneWidget);
    expect(repository.signInCalls, 0);
    await tester.pump(const Duration(seconds: 1)); // let the staggered entrance delays run out
    await tester.pumpWidget(const SizedBox.shrink());
  });

  testWidgets('a rejected verification code is cleared so it can be typed again', (tester) async {
    final repository = _FakeAuthRepository(throwOnSignIn: const InvalidOtpException());
    _usePhoneScreen(tester);
    await tester.pumpWidget(
      _wrap(
        repository,
        home: const OtpPage(phone: '254712345678', pin: '2468', expiresInSeconds: 300),
      ),
    );
    await tester.pump();

    await tester.enterText(find.byType(TextFormField), '123456');
    await tester.pump(const Duration(milliseconds: 350)); // pin_code_fields waits 300ms before onCompleted
    await tester.pump();
    await tester.pump();

    expect(repository.signInCalls, 1);
    expect(find.text('That code is incorrect or has expired.'), findsOneWidget);
    expect(find.text('1'), findsNothing, reason: 'the rejected digits are gone from the boxes');
    await tester.pump(const Duration(seconds: 1)); // let the staggered entrance delays run out
    await tester.pumpWidget(const SizedBox.shrink());
  });
}
