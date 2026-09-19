import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:sacco_member/core/device/biometric_service.dart';
import 'package:sacco_member/core/storage/secure_storage.dart';
import 'package:sacco_member/features/accounts/presentation/providers/balance_privacy.dart';
import 'package:sacco_member/features/accounts/presentation/widgets/balance_reveal.dart';

class _FakeStorage extends SecureStorage {
  _FakeStorage({this.biometrics = false});

  bool requirePin = true;
  bool biometrics;

  @override
  Future<bool> readRequirePinForBalances() async => requirePin;
  @override
  Future<void> saveRequirePinForBalances(bool value) async => requirePin = value;
  @override
  Future<bool> readBiometricsForBalances() async => biometrics;
  @override
  Future<void> saveBiometricsForBalances(bool value) async => biometrics = value;
}

class _FakeBiometrics extends BiometricService {
  _FakeBiometrics({this.enrolled = true, this.succeeds = true});

  final bool enrolled;
  bool succeeds;
  int prompts = 0;
  bool? lastBiometricOnly;

  @override
  Future<bool> hasEnrolledBiometrics() async => enrolled;
  @override
  Future<String> label() async => 'fingerprint';
  @override
  Future<bool> authenticate({String reason = '', bool biometricOnly = false}) async {
    prompts++;
    lastBiometricOnly = biometricOnly;
    return succeeds;
  }
}

Future<BalancePrivacyController> _controller(_FakeStorage storage, _FakeBiometrics biometrics) async {
  final controller = BalancePrivacyController(storage, biometrics);
  await Future<void>.delayed(Duration.zero);
  return controller;
}

void main() {
  group('BalancePrivacyController', () {
    test('turning on the fingerprint option saves it only after the fingerprint is confirmed', () async {
      final storage = _FakeStorage();
      final biometrics = _FakeBiometrics(succeeds: false);
      final controller = await _controller(storage, biometrics);

      expect(await controller.setUseBiometrics(true), isFalse);
      expect(storage.biometrics, isFalse);
      expect(controller.state.useBiometrics, isFalse);

      biometrics.succeeds = true;
      expect(await controller.setUseBiometrics(true), isTrue);
      expect(storage.biometrics, isTrue);
      expect(controller.state.canUnlockWithBiometrics, isTrue);
      expect(biometrics.lastBiometricOnly, isTrue, reason: "the phone's own screen lock must not stand in");
    });

    test('turning off "Require PIN" also drops the fingerprint option', () async {
      final storage = _FakeStorage(biometrics: true);
      final controller = await _controller(storage, _FakeBiometrics());
      expect(controller.state.canUnlockWithBiometrics, isTrue);

      await controller.setRequirePin(false);

      expect(storage.biometrics, isFalse);
      expect(controller.state.useBiometrics, isFalse);
    });

    test('with no fingerprint enrolled the saved option is ignored and the PIN is used', () async {
      final biometrics = _FakeBiometrics(enrolled: false);
      final controller = await _controller(_FakeStorage(biometrics: true), biometrics);

      expect(controller.state.canUnlockWithBiometrics, isFalse);
      expect(await controller.unlockWithBiometrics(), isFalse);
      expect(biometrics.prompts, 0);
      expect(controller.state.hidden, isTrue);
    });
  });

  group('ensureBalancesUnlocked', () {
    Future<(ProviderContainer, BuildContext, WidgetRef)> pump(
      WidgetTester tester,
      _FakeStorage storage,
      _FakeBiometrics biometrics,
    ) async {
      late BuildContext ctx;
      late WidgetRef widgetRef;
      final container = ProviderContainer(
        overrides: [balancePrivacyProvider.overrideWith((ref) => BalancePrivacyController(storage, biometrics))],
      );
      addTearDown(container.dispose);
      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: MaterialApp(
            home: Consumer(
              builder: (context, ref, _) {
                ctx = context;
                widgetRef = ref;
                ref.watch(balancePrivacyProvider);
                return const Scaffold();
              },
            ),
          ),
        ),
      );
      await tester.pump();
      return (container, ctx, widgetRef);
    }

    testWidgets('a matching fingerprint shows balances without the PIN sheet', (tester) async {
      final biometrics = _FakeBiometrics();
      final (container, context, ref) = await pump(tester, _FakeStorage(biometrics: true), biometrics);

      final unlocked = ensureBalancesUnlocked(context, ref);
      await tester.pumpAndSettle();

      expect(await unlocked, isTrue);
      expect(find.text('Enter your PIN'), findsNothing);
      expect(container.read(balancePrivacyProvider).hidden, isFalse);
    });

    testWidgets('a cancelled fingerprint falls back to the PIN sheet, which offers the fingerprint again', (
      tester,
    ) async {
      final biometrics = _FakeBiometrics(succeeds: false);
      final (container, context, ref) = await pump(tester, _FakeStorage(biometrics: true), biometrics);

      final unlocked = ensureBalancesUnlocked(context, ref);
      await tester.pumpAndSettle();
      expect(find.text('Enter your PIN'), findsOneWidget);
      expect(find.text('Use fingerprint instead'), findsOneWidget);

      biometrics.succeeds = true;
      await tester.tap(find.text('Use fingerprint instead'));
      await tester.pumpAndSettle();

      expect(await unlocked, isTrue);
      expect(biometrics.prompts, 2);
      expect(container.read(balancePrivacyProvider).hidden, isFalse);
    });

    testWidgets('changing the privacy settings asks for the PIN, never the fingerprint', (tester) async {
      final biometrics = _FakeBiometrics();
      final (_, context, ref) = await pump(tester, _FakeStorage(biometrics: true), biometrics);

      confirmPinForPrivacyChange(context, ref);
      await tester.pumpAndSettle();

      expect(find.text('Enter your PIN'), findsOneWidget);
      expect(find.text('Use fingerprint instead'), findsNothing);
      expect(biometrics.prompts, 0);
    });
  });
}
