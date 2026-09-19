import '../../../../core/models/enums.dart';

class Withdrawal {
  const Withdrawal({
    required this.id,
    required this.accountNumber,
    required this.memberId,
    required this.amount,
    required this.fee,
    required this.channel,
    required this.status,
    required this.requestedAt,
    required this.noticeExpiresOn,
    this.payoutDestination,
    this.approvedAt,
    this.paidAt,
    this.rejectionReason,
    this.narrative,
  });

  factory Withdrawal.fromJson(Map<String, dynamic> json) => Withdrawal(
    id: json['id'] as String,
    accountNumber: json['accountNumber'] as String,
    memberId: json['memberId'] as String,
    amount: (json['amount'] as num).toDouble(),
    fee: (json['fee'] as num).toDouble(),
    channel: PayoutChannel.fromWire(json['channel'] as String),
    payoutDestination: json['payoutDestination'] as String?,
    status: WithdrawalStatus.fromWire(json['status'] as String),
    requestedAt: DateTime.parse(json['requestedAt'] as String),
    noticeExpiresOn: DateTime.parse(json['noticeExpiresOn'] as String),
    approvedAt: json['approvedAt'] == null ? null : DateTime.parse(json['approvedAt'] as String),
    paidAt: json['paidAt'] == null ? null : DateTime.parse(json['paidAt'] as String),
    rejectionReason: json['rejectionReason'] as String?,
    narrative: json['narrative'] as String?,
  );

  final String id;
  final String accountNumber;
  final String memberId;
  final double amount;
  final double fee;
  final PayoutChannel channel;
  final String? payoutDestination;
  final WithdrawalStatus status;
  final DateTime requestedAt;
  final DateTime noticeExpiresOn;
  final DateTime? approvedAt;
  final DateTime? paidAt;
  final String? rejectionReason;
  final String? narrative;
}
