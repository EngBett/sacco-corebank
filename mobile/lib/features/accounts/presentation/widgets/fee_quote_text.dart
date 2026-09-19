import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/theme/app_theme.dart';
import '../../../../core/utils/formatters.dart';
import '../../domain/entities/fees.dart';
import '../providers/accounts_providers.dart';

/// "Fee: KES 45 (4 bands)" under a deposit/withdrawal form, priced by the server's fee matrix as the member types.
class FeeQuoteText extends ConsumerStatefulWidget {
  const FeeQuoteText({
    super.key,
    required this.type,
    required this.accountNumber,
    required this.channel,
    required this.amount,
  });

  final FeeTransactionType type;
  final String? accountNumber;

  /// Fee-matrix channel wire name: Cash, MPesa, AirtelMoney, BankTransfer.
  final String channel;
  final double? amount;

  @override
  ConsumerState<FeeQuoteText> createState() => _FeeQuoteTextState();
}

class _FeeQuoteTextState extends ConsumerState<FeeQuoteText> {
  Timer? _debounce;
  double? _settledAmount;

  @override
  void initState() {
    super.initState();
    _settledAmount = widget.amount;
  }

  @override
  void didUpdateWidget(FeeQuoteText old) {
    super.didUpdateWidget(old);
    if (old.amount == widget.amount) return;
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 400), () {
      if (mounted) setState(() => _settledAmount = widget.amount);
    });
  }

  @override
  void dispose() {
    _debounce?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final account = widget.accountNumber;
    final amount = _settledAmount;
    if (account == null || amount == null || amount <= 0) return const SizedBox.shrink();

    final quote = ref.watch(feeQuoteProvider((widget.type, account, widget.channel, amount)));
    const style = TextStyle(color: AppTheme.textSecondary, fontSize: 13);
    return Padding(
      padding: const EdgeInsets.only(top: 8),
      child: quote.when(
        data: (q) => Text(
          q.fee > 0
              ? 'Fee: ${Formatters.money(q.fee)} (${q.basis})'
              : 'No fee for this ${widget.type == FeeTransactionType.deposit ? 'deposit' : 'withdrawal'}',
          style: style,
        ),
        loading: () => const Text('Checking fee…', style: style),
        error: (_, _) => const SizedBox.shrink(),
      ),
    );
  }
}
