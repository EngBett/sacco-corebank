import 'savings_account.dart';

/// What a transaction would cost right now, priced by the SACCO's fee matrix (ADR 0015).
class FeeQuote {
  const FeeQuote({required this.fee, required this.basis});

  factory FeeQuote.fromJson(Map<String, dynamic> json) =>
      FeeQuote(fee: (json['fee'] as num).toDouble(), basis: json['basis'] as String);

  final double fee;

  /// e.g. "2% (min 10)", "4 bands", "Product default", "No fee".
  final String basis;
}

/// Fee-matrix transaction types, by their API wire names.
enum FeeTransactionType {
  deposit('Deposit'),
  withdrawal('Withdrawal');

  const FeeTransactionType(this.wireName);
  final String wireName;
}

/// Result of paying to reveal a fee-gated balance.
class BalanceReveal {
  const BalanceReveal({required this.charged, required this.fee, required this.account, this.visibleUntil});

  factory BalanceReveal.fromJson(Map<String, dynamic> json) => BalanceReveal(
    charged: json['charged'] as bool,
    fee: (json['fee'] as num).toDouble(),
    visibleUntil: json['visibleUntil'] == null ? null : DateTime.parse(json['visibleUntil'] as String),
    account: SavingsAccount.fromJson(json['account'] as Map<String, dynamic>),
  );

  final bool charged;
  final double fee;
  final DateTime? visibleUntil;
  final SavingsAccount account;
}

enum PinCheckResult { valid, invalid, lockedOut }
