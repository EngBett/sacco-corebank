import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:uuid/uuid.dart';

import '../../../../core/theme/app_theme.dart';
import '../../../../core/utils/formatters.dart';
import '../../domain/entities/fees.dart';
import '../../domain/entities/savings_account.dart';
import '../providers/accounts_providers.dart';
import '../providers/balance_privacy.dart';

const _masked = 'KES ••••••';

/// A balance as the member may currently see it: masked while PIN privacy is on or while it is fee-locked (null).
class BalanceText extends ConsumerWidget {
  const BalanceText(this.amount, {super.key, this.style});

  final double? amount;
  final TextStyle? style;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final hidden = ref.watch(balancePrivacyProvider).hidden;
    final figures = (style ?? const TextStyle()).copyWith(fontFeatures: AppTheme.moneyFigures);
    if (hidden || amount == null) return Text(_masked, style: figures);
    return Text(Formatters.money(amount!), style: figures);
  }
}

/// Asks for the fingerprint (when the member opted in) or the PIN when "Require PIN to show balances" is on and
/// balances haven't been unlocked this session. A cancelled or failed fingerprint falls back to the PIN sheet.
/// Returns true when balances may be shown.
Future<bool> ensureBalancesUnlocked(BuildContext context, WidgetRef ref) async {
  final privacy = ref.read(balancePrivacyProvider);
  if (!privacy.hidden) return true;
  if (privacy.canUnlockWithBiometrics && await ref.read(balancePrivacyProvider.notifier).unlockWithBiometrics()) {
    return true;
  }
  if (!context.mounted) return false;
  return _showPinSheet(
    context,
    ref,
    message: 'Your balances are hidden on this device until you enter your PIN.',
    actionLabel: 'Show balances',
    allowBiometrics: privacy.canUnlockWithBiometrics,
  );
}

/// Always asks for the SACCO PIN — never a fingerprint — before changing how balances are protected, even when they
/// are already showing. A fingerprint can't be used to turn off or replace the PIN it stands in for.
Future<bool> confirmPinForPrivacyChange(BuildContext context, WidgetRef ref) => _showPinSheet(
  context,
  ref,
  message: 'Enter your PIN to change how your balances are protected.',
  actionLabel: 'Confirm',
  allowBiometrics: false,
);

/// Label for "show balances" buttons: names the fingerprint when that's what will be asked for.
String unlockActionLabel(BalancePrivacyState privacy) =>
    privacy.canUnlockWithBiometrics ? 'Use ${privacy.biometricLabel}' : 'Enter PIN';

IconData biometricIcon(String label) => label == 'Face ID' ? Icons.face_rounded : Icons.fingerprint_rounded;

Future<bool> _showPinSheet(
  BuildContext context,
  WidgetRef ref, {
  required String message,
  required String actionLabel,
  required bool allowBiometrics,
}) async {
  final ok = await showModalBottomSheet<bool>(
    context: context,
    isScrollControlled: true,
    backgroundColor: AppTheme.surface,
    shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(24))),
    builder: (_) => _PinSheet(message: message, actionLabel: actionLabel, allowBiometrics: allowBiometrics),
  );
  if (ok == true) ref.read(balancePrivacyProvider.notifier).unlock();
  return ok == true;
}

/// Pays the account's balance-enquiry fee (after confirming) so its balance shows for a short window.
Future<bool> revealFeeLockedBalance(BuildContext context, WidgetRef ref, SavingsAccount account) async {
  if (!account.balanceLocked) return true;
  final fee = account.balanceEnquiryFee ?? 0;
  final confirmed = await showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      title: const Text('View this balance?'),
      content: Text(
        fee > 0
            ? 'Your SACCO charges ${Formatters.money(fee)} to view the ${account.kind.label} balance. '
                  "It's taken from your FOSA account and the balance stays visible for a few minutes."
            : 'This balance will stay visible for a few minutes.',
      ),
      actions: [
        TextButton(onPressed: () => Navigator.of(context).pop(false), child: const Text('Cancel')),
        FilledButton(onPressed: () => Navigator.of(context).pop(true), child: Text(fee > 0 ? 'Pay & view' : 'View')),
      ],
    ),
  );
  if (confirmed != true || !context.mounted) return false;
  try {
    await ref.read(accountsRepositoryProvider).revealBalance(account.accountNumber, const Uuid().v4());
    ref.invalidate(myAccountsProvider);
    ref.invalidate(mySummaryProvider);
    return true;
  } on DioException catch (e) {
    if (context.mounted) {
      // ProblemDetails carries the domain code in `title`.
      final code = (e.response?.data is Map) ? (e.response!.data as Map)['title'] : null;
      final message = switch (code) {
        'ledger.insufficient_funds' => 'Your FOSA account doesn\'t have enough to cover the fee.',
        'savings.balance_enquiry.no_fosa' => 'You need an active FOSA account to pay the balance enquiry fee.',
        _ => "Couldn't show that balance. Please try again.",
      };
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
    }
    return false;
  }
}

/// Opening a statement shows running balances, so it goes through both gates first.
Future<void> openStatement(BuildContext context, WidgetRef ref, SavingsAccount account) async {
  if (!await ensureBalancesUnlocked(context, ref) || !context.mounted) return;
  if (!await revealFeeLockedBalance(context, ref, account) || !context.mounted) return;
  context.push('/statement/${account.accountNumber}');
}

class _PinSheet extends ConsumerStatefulWidget {
  const _PinSheet({required this.message, required this.actionLabel, required this.allowBiometrics});

  final String message;
  final String actionLabel;
  final bool allowBiometrics;

  @override
  ConsumerState<_PinSheet> createState() => _PinSheetState();
}

class _PinSheetState extends ConsumerState<_PinSheet> {
  final _controller = TextEditingController();
  bool _checking = false;
  String? _error;

  Future<void> _useBiometrics() async {
    final ok = await ref.read(balancePrivacyProvider.notifier).unlockWithBiometrics();
    if (ok && mounted) Navigator.of(context).pop(true);
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final pin = _controller.text.trim();
    if (pin.length < 4) {
      setState(() => _error = 'Enter your 4–6 digit PIN');
      return;
    }
    setState(() {
      _checking = true;
      _error = null;
    });
    try {
      final result = await ref.read(accountsRepositoryProvider).verifyPin(pin);
      if (!mounted) return;
      switch (result) {
        case PinCheckResult.valid:
          Navigator.of(context).pop(true);
        case PinCheckResult.invalid:
          _controller.clear();
          setState(() => _error = 'That PIN is incorrect.');
        case PinCheckResult.lockedOut:
          _controller.clear();
          setState(() => _error = 'Too many wrong attempts. Try again in 30 minutes.');
      }
    } catch (_) {
      if (mounted) setState(() => _error = "Couldn't check your PIN. Check your connection.");
    } finally {
      if (mounted) setState(() => _checking = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.fromLTRB(24, 24, 24, 24 + MediaQuery.of(context).viewInsets.bottom),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Enter your PIN', style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 6),
          Text(widget.message, style: const TextStyle(color: AppTheme.textSecondary)),
          const SizedBox(height: 20),
          TextField(
            controller: _controller,
            autofocus: true,
            obscureText: true,
            maxLength: 6,
            keyboardType: TextInputType.number,
            decoration: InputDecoration(labelText: 'PIN', counterText: '', errorText: _error),
            onSubmitted: (_) => _submit(),
          ),
          const SizedBox(height: 16),
          FilledButton(
            onPressed: _checking ? null : _submit,
            child: _checking
                ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2))
                : Text(widget.actionLabel),
          ),
          if (widget.allowBiometrics) ...[
            const SizedBox(height: 8),
            TextButton.icon(
              onPressed: _checking ? null : _useBiometrics,
              icon: Icon(biometricIcon(ref.watch(balancePrivacyProvider).biometricLabel)),
              label: Text('Use ${ref.watch(balancePrivacyProvider).biometricLabel} instead'),
            ),
          ],
        ],
      ),
    );
  }
}
