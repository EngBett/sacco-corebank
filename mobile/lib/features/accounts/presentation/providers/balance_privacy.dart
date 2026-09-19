import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/device/biometric_service.dart';
import '../../../../core/di/service_locator.dart';
import '../../../../core/storage/secure_storage.dart';

class BalancePrivacyState {
  const BalancePrivacyState({
    required this.requirePin,
    required this.unlocked,
    this.useBiometrics = false,
    this.biometricsAvailable = false,
    this.biometricLabel = 'fingerprint',
  });

  /// Member's device preference: hide every balance until the PIN is re-entered.
  final bool requirePin;

  /// PIN (or biometric) accepted this session. Cleared when the app goes to the background.
  final bool unlocked;

  /// Member opted to show hidden balances with a fingerprint instead of typing the PIN.
  final bool useBiometrics;

  /// This phone has a fingerprint or face enrolled, so the option can be offered at all.
  final bool biometricsAvailable;

  /// "fingerprint" or "Face ID", for copy.
  final String biometricLabel;

  bool get hidden => requirePin && !unlocked;

  /// The biometric prompt is tried before the PIN sheet only while all three hold — if the member removes every
  /// enrolled fingerprint the app quietly goes back to the PIN.
  bool get canUnlockWithBiometrics => requirePin && useBiometrics && biometricsAvailable;

  BalancePrivacyState copyWith({
    bool? requirePin,
    bool? unlocked,
    bool? useBiometrics,
    bool? biometricsAvailable,
    String? biometricLabel,
  }) => BalancePrivacyState(
    requirePin: requirePin ?? this.requirePin,
    unlocked: unlocked ?? this.unlocked,
    useBiometrics: useBiometrics ?? this.useBiometrics,
    biometricsAvailable: biometricsAvailable ?? this.biometricsAvailable,
    biometricLabel: biometricLabel ?? this.biometricLabel,
  );
}

/// "Require PIN to show balances" is a shoulder-surfing guard on an already signed-in phone, so it lives on the device.
/// It is separate from balance-enquiry fees, which the server enforces (ADR 0015). The fingerprint option is likewise
/// device-local: it only replaces typing the PIN for this guard, never for anything the server checks.
class BalancePrivacyController extends StateNotifier<BalancePrivacyState> {
  BalancePrivacyController(this._storage, this._biometrics)
    : super(const BalancePrivacyState(requirePin: false, unlocked: false)) {
    _load();
  }

  final SecureStorage _storage;
  final BiometricService _biometrics;

  Future<void> _load() async {
    final requirePin = await _storage.readRequirePinForBalances();
    final useBiometrics = await _storage.readBiometricsForBalances();
    final available = await _biometrics.hasEnrolledBiometrics();
    final label = available ? await _biometrics.label() : 'fingerprint';
    if (!mounted) return;
    state = state.copyWith(
      requirePin: requirePin,
      useBiometrics: useBiometrics,
      biometricsAvailable: available,
      biometricLabel: label,
    );
  }

  /// Turning the guard off also drops the fingerprint option, so switching it back on starts from PIN-only.
  Future<void> setRequirePin(bool value) async {
    await _storage.saveRequirePinForBalances(value);
    if (!value) await _storage.saveBiometricsForBalances(false);
    state = state.copyWith(requirePin: value, unlocked: false, useBiometrics: value && state.useBiometrics);
  }

  /// Enabling asks for the fingerprint once, so the member knows it works before relying on it.
  /// Returns false (nothing saved) when that check is cancelled or fails.
  Future<bool> setUseBiometrics(bool value) async {
    if (value) {
      final ok = await _biometrics.authenticate(
        reason: 'Confirm your ${state.biometricLabel} to use it for showing balances',
        biometricOnly: true,
      );
      if (!ok) return false;
    }
    await _storage.saveBiometricsForBalances(value);
    if (mounted) state = state.copyWith(useBiometrics: value);
    return true;
  }

  /// Shows balances after a successful biometric check. No fallback to the phone's own screen lock — the PIN sheet
  /// is the fallback.
  Future<bool> unlockWithBiometrics() async {
    if (!state.canUnlockWithBiometrics) return false;
    final ok = await _biometrics.authenticate(reason: 'Show your balances', biometricOnly: true);
    if (ok && mounted) unlock();
    return ok;
  }

  void unlock() => state = state.copyWith(unlocked: true);

  void lock() => state = state.copyWith(unlocked: false);
}

final balancePrivacyProvider = StateNotifierProvider<BalancePrivacyController, BalancePrivacyState>(
  (ref) => BalancePrivacyController(sl<SecureStorage>(), sl<BiometricService>()),
);
