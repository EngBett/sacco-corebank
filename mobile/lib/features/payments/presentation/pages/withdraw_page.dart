import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/models/enums.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../../accounts/domain/entities/fees.dart';
import '../../../accounts/presentation/providers/accounts_providers.dart';
import '../../../accounts/presentation/widgets/fee_quote_text.dart';
import '../providers/payments_providers.dart';

/// Withdrawal channels the app offers (ADR 0008): cash is excluded here — it's
/// a branch-teller flow, not something a member requests from their phone.
const _withdrawChannels = [PayoutChannel.mpesa, PayoutChannel.airtelMoney, PayoutChannel.bankTransfer];

class WithdrawPage extends ConsumerStatefulWidget {
  const WithdrawPage({super.key});

  @override
  ConsumerState<WithdrawPage> createState() => _WithdrawPageState();
}

class _WithdrawPageState extends ConsumerState<WithdrawPage> {
  final _formKey = GlobalKey<FormState>();
  final _amountController = TextEditingController();
  final _destinationController = TextEditingController();
  String? _accountNumber;
  PayoutChannel _channel = PayoutChannel.mpesa;
  bool _submitting = false;
  String? _resultMessage;

  @override
  void dispose() {
    _amountController.dispose();
    _destinationController.dispose();
    super.dispose();
  }

  String get _destinationLabel => switch (_channel) {
    PayoutChannel.mpesa || PayoutChannel.airtelMoney => 'Phone number to receive funds',
    PayoutChannel.bankTransfer => 'Bank account number',
    PayoutChannel.cash => 'Destination',
  };

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate() || _accountNumber == null) return;
    setState(() {
      _submitting = true;
      _resultMessage = null;
    });
    try {
      await ref
          .read(paymentsRepositoryProvider)
          .requestWithdrawal(
            accountNumber: _accountNumber!,
            amount: double.parse(_amountController.text),
            channel: _channel,
            destination: _destinationController.text.trim(),
          );
      ref.invalidate(myWithdrawalsProvider);
      if (!mounted) return;
      setState(() => _resultMessage = 'success');
    } catch (_) {
      if (!mounted) return;
      setState(() => _resultMessage = 'error');
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final accounts = ref.watch(myAccountsProvider);

    if (_resultMessage == 'success') {
      return Scaffold(
        appBar: AppBar(title: const Text('Withdraw')),
        body: Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(Icons.hourglass_top, size: 64, color: Theme.of(context).colorScheme.primary),
                const SizedBox(height: 16),
                const Text(
                  'Your withdrawal request has been submitted and is pending approval by SACCO staff. '
                  "You'll see it move to Approved and then Paid in your payments history.",
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 24),
                FilledButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Done')),
              ],
            ),
          ),
        ),
      );
    }

    return Scaffold(
      appBar: AppBar(title: const Text('Withdraw')),
      body: AsyncValueView(
        value: accounts,
        data: (context, allAccounts) {
          // Share capital isn't withdrawable directly — it can only be transferred or sold to
          // another member (enforced by the backend too: savings.shares.no_direct_withdrawal).
          final list = allAccounts.where((a) => a.kind != ProductKind.shares).toList();
          return Form(
            key: _formKey,
            child: ListView(
              padding: const EdgeInsets.all(16),
              children: [
                DropdownButtonFormField<String>(
                  value: _accountNumber ??= list.isEmpty ? null : list.first.accountNumber,
                  decoration: const InputDecoration(labelText: 'Withdraw from'),
                  items: list
                      .map(
                        (a) => DropdownMenuItem(
                          value: a.accountNumber,
                          child: Text('${a.kind.label} · ${a.accountNumber}'),
                        ),
                      )
                      .toList(),
                  onChanged: (value) => setState(() => _accountNumber = value),
                  validator: (value) => value == null ? 'Choose an account' : null,
                ),
                const SizedBox(height: 16),
                TextFormField(
                  controller: _amountController,
                  keyboardType: const TextInputType.numberWithOptions(decimal: true),
                  decoration: const InputDecoration(labelText: 'Amount (KES)'),
                  validator: (value) {
                    final amount = double.tryParse(value ?? '');
                    if (amount == null || amount <= 0) return 'Enter a valid amount';
                    return null;
                  },
                  onChanged: (_) => setState(() {}),
                ),
                const SizedBox(height: 16),
                Text('Pay out via', style: Theme.of(context).textTheme.titleSmall),
                const SizedBox(height: 8),
                ..._withdrawChannels.map(
                  (c) => RadioListTile<PayoutChannel>(
                    value: c,
                    groupValue: _channel,
                    onChanged: (value) => setState(() => _channel = value!),
                    title: Text(c.label),
                  ),
                ),
                const SizedBox(height: 8),
                TextFormField(
                  controller: _destinationController,
                  decoration: InputDecoration(labelText: _destinationLabel),
                  validator: (value) => (value == null || value.trim().isEmpty) ? 'Required' : null,
                ),
                FeeQuoteText(
                  type: FeeTransactionType.withdrawal,
                  accountNumber: _accountNumber,
                  channel: _channel.wireName,
                  amount: double.tryParse(_amountController.text),
                ),
                const SizedBox(height: 8),
                Text(
                  'This still needs approval from SACCO staff before it is paid out — that protects your '
                  'account even if this phone is lost.',
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(color: AppTheme.textSecondary),
                ),
                if (_resultMessage == 'error')
                  const Padding(
                    padding: EdgeInsets.only(top: 8),
                    child: Text(
                      'Could not submit the withdrawal. Please try again.',
                      style: TextStyle(color: Colors.redAccent),
                    ),
                  ),
                const SizedBox(height: 24),
                FilledButton(
                  onPressed: _submitting || list.isEmpty ? null : _submit,
                  child: _submitting
                      ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2))
                      : const Text('Request withdrawal'),
                ),
              ],
            ),
          );
        },
      ),
    );
  }
}
