import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/models/enums.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../../accounts/domain/entities/fees.dart';
import '../../../accounts/presentation/providers/accounts_providers.dart';
import '../../../accounts/presentation/widgets/fee_quote_text.dart';
import '../providers/payments_providers.dart';

class _DepositProviderOption {
  const _DepositProviderOption(this.value, this.label, this.icon);
  final String value;
  final String label;
  final IconData icon;
}

/// Deposit channels the app offers (ADR 0008): M-Pesa, Airtel Money, Equity.
/// NCBA never appears here — NcbaProvider is payout-only.
const _depositProviders = [
  _DepositProviderOption(ProviderNames.mpesa, 'M-Pesa', Icons.phone_android),
  _DepositProviderOption(ProviderNames.airtelMoney, 'Airtel Money', Icons.phone_android),
  _DepositProviderOption(ProviderNames.equity, 'Equity Bank', Icons.account_balance),
];

class DepositPage extends ConsumerStatefulWidget {
  const DepositPage({super.key});

  @override
  ConsumerState<DepositPage> createState() => _DepositPageState();
}

class _DepositPageState extends ConsumerState<DepositPage> {
  final _formKey = GlobalKey<FormState>();
  final _amountController = TextEditingController();
  String? _accountNumber;
  String _provider = _depositProviders.first.value;
  bool _submitting = false;
  String? _resultMessage;

  @override
  void dispose() {
    _amountController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate() || _accountNumber == null) return;
    setState(() {
      _submitting = true;
      _resultMessage = null;
    });
    try {
      await ref
          .read(paymentsRepositoryProvider)
          .topUp(provider: _provider, amount: double.parse(_amountController.text), accountNumber: _accountNumber!);
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
        appBar: AppBar(title: const Text('Deposit')),
        body: Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(Icons.check_circle, size: 64, color: Colors.green.shade600),
                const SizedBox(height: 16),
                const Text(
                  "We've sent a payment request to your phone. Confirm it there to complete the deposit.",
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
      appBar: AppBar(title: const Text('Deposit')),
      body: AsyncValueView(
        value: accounts,
        data: (context, list) => Form(
          key: _formKey,
          child: ListView(
            padding: const EdgeInsets.all(16),
            children: [
              DropdownButtonFormField<String>(
                value: _accountNumber ??= list.isEmpty ? null : list.first.accountNumber,
                decoration: const InputDecoration(labelText: 'Deposit into'),
                items: list
                    .map(
                      (a) =>
                          DropdownMenuItem(value: a.accountNumber, child: Text('${a.kind.label} · ${a.accountNumber}')),
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
              Text('Pay with', style: Theme.of(context).textTheme.titleSmall),
              const SizedBox(height: 8),
              ..._depositProviders.map(
                (p) => RadioListTile<String>(
                  value: p.value,
                  groupValue: _provider,
                  onChanged: (value) => setState(() => _provider = value!),
                  title: Text(p.label),
                  secondary: Icon(p.icon),
                ),
              ),
              FeeQuoteText(
                type: FeeTransactionType.deposit,
                accountNumber: _accountNumber,
                // Bank collections post as bank-transfer deposits (PaymentFinalizer), so they're priced on that channel.
                channel: _provider == ProviderNames.equity ? 'BankTransfer' : _provider,
                amount: double.tryParse(_amountController.text),
              ),
              if (_resultMessage == 'error')
                const Padding(
                  padding: EdgeInsets.only(top: 8),
                  child: Text(
                    'Could not start the deposit. Please try again.',
                    style: TextStyle(color: Colors.redAccent),
                  ),
                ),
              const SizedBox(height: 24),
              FilledButton(
                onPressed: _submitting || list.isEmpty ? null : _submit,
                child: _submitting
                    ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2))
                    : const Text('Send deposit request'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
