import '../../../../core/models/enums.dart';

class SavingsAccount {
  const SavingsAccount({
    required this.id,
    required this.accountNumber,
    required this.memberId,
    required this.productCode,
    required this.kind,
    required this.status,
    required this.openedAt,
    required this.balance,
    required this.heldAmount,
    required this.availableBalance,
    this.principal,
    this.termMonths,
    this.interestRateBps,
    this.maturityDate,
    this.payoutAccountNumber,
    this.balanceLocked = false,
    this.balanceEnquiryFee,
    this.balanceVisibleUntil,
  });

  factory SavingsAccount.fromJson(Map<String, dynamic> json) => SavingsAccount(
    id: json['id'] as String,
    accountNumber: json['accountNumber'] as String,
    memberId: json['memberId'] as String,
    productCode: json['productCode'] as String,
    kind: ProductKind.fromWire(json['kind'] as String),
    status: SavingsAccountStatus.fromWire(json['status'] as String),
    openedAt: DateTime.parse(json['openedAt'] as String),
    balance: (json['balance'] as num?)?.toDouble(),
    heldAmount: (json['heldAmount'] as num?)?.toDouble(),
    availableBalance: (json['availableBalance'] as num?)?.toDouble(),
    principal: (json['principal'] as num?)?.toDouble(),
    termMonths: json['termMonths'] as int?,
    interestRateBps: json['interestRateBps'] as int?,
    maturityDate: json['maturityDate'] == null ? null : DateTime.parse(json['maturityDate'] as String),
    payoutAccountNumber: json['payoutAccountNumber'] as String?,
    balanceLocked: json['balanceLocked'] as bool? ?? false,
    balanceEnquiryFee: (json['balanceEnquiryFee'] as num?)?.toDouble(),
    balanceVisibleUntil: json['balanceVisibleUntil'] == null
        ? null
        : DateTime.parse(json['balanceVisibleUntil'] as String),
  );

  final String id;
  final String accountNumber;
  final String memberId;
  final String productCode;
  final ProductKind kind;
  final SavingsAccountStatus status;
  final DateTime openedAt;

  /// Null while [balanceLocked]: the product carries a balance-enquiry fee and no paid reveal window is open (ADR 0015).
  final double? balance;
  final double? heldAmount;
  final double? availableBalance;
  final double? principal;
  final int? termMonths;
  final int? interestRateBps;
  final DateTime? maturityDate;
  final String? payoutAccountNumber;

  final bool balanceLocked;
  final double? balanceEnquiryFee;
  final DateTime? balanceVisibleUntil;
}
