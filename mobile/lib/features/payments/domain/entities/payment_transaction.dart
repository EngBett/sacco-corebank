import '../../../../core/models/enums.dart';

class PaymentTransaction {
  const PaymentTransaction({
    required this.id,
    required this.provider,
    required this.status,
    required this.amount,
    required this.ourReference,
    required this.initiatedAt,
    this.accountNumber,
    this.loanNumber,
    this.failureReason,
    this.completedAt,
    this.narrative,
  });

  factory PaymentTransaction.fromJson(Map<String, dynamic> json) => PaymentTransaction(
    id: json['id'] as String,
    provider: json['provider'] as String,
    status: PaymentStatus.fromWire(json['status'] as String),
    amount: (json['amount'] as num).toDouble(),
    ourReference: json['ourReference'] as String,
    accountNumber: json['accountNumber'] as String?,
    loanNumber: json['loanNumber'] as String?,
    failureReason: json['failureReason'] as String?,
    initiatedAt: DateTime.parse(json['initiatedAt'] as String),
    completedAt: json['completedAt'] == null ? null : DateTime.parse(json['completedAt'] as String),
    narrative: json['narrative'] as String?,
  );

  final String id;
  final String provider;
  final PaymentStatus status;
  final double amount;
  final String ourReference;
  final String? accountNumber;
  final String? loanNumber;
  final String? failureReason;
  final DateTime initiatedAt;
  final DateTime? completedAt;
  final String? narrative;
}
