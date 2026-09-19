class MemberSavingsSummary {
  const MemberSavingsSummary({
    required this.memberId,
    required this.bosaDeposits,
    required this.shares,
    required this.fosaBalance,
    required this.fixedDeposits,
    required this.monthsWithContributions,
    required this.hasLockedBalances,
    this.firstContributionDate,
    this.fosaAccountNumber,
    this.bosaDepositAccountNumber,
    this.sharesAccountNumber,
  });

  factory MemberSavingsSummary.fromJson(Map<String, dynamic> json) => MemberSavingsSummary(
    memberId: json['memberId'] as String,
    bosaDeposits: (json['bosaDeposits'] as num?)?.toDouble(),
    shares: (json['shares'] as num?)?.toDouble(),
    fosaBalance: (json['fosaBalance'] as num?)?.toDouble(),
    fixedDeposits: (json['fixedDeposits'] as num?)?.toDouble(),
    monthsWithContributions: json['monthsWithContributions'] as int,
    hasLockedBalances: json['hasLockedBalances'] as bool? ?? false,
    firstContributionDate: json['firstContributionDate'] == null
        ? null
        : DateTime.parse(json['firstContributionDate'] as String),
    fosaAccountNumber: json['fosaAccountNumber'] as String?,
    bosaDepositAccountNumber: json['bosaDepositAccountNumber'] as String?,
    sharesAccountNumber: json['sharesAccountNumber'] as String?,
  );

  /// Each bucket is null when an account in it is fee-locked (ADR 0015).
  final String memberId;
  final double? bosaDeposits;
  final double? shares;
  final double? fosaBalance;
  final double? fixedDeposits;
  final int monthsWithContributions;
  final bool hasLockedBalances;
  final DateTime? firstContributionDate;
  final String? fosaAccountNumber;
  final String? bosaDepositAccountNumber;
  final String? sharesAccountNumber;

  /// Sum of the balances the member can currently see.
  double get visibleTotal =>
      [bosaDeposits, shares, fosaBalance, fixedDeposits].whereType<double>().fold(0, (a, b) => a + b);
}
