import '../../../../core/models/enums.dart';

/// A member's offer to sell part of their share capital to another member (ADR 0008 follow-up:
/// shares aren't withdrawable, only sold). Settlement (FOSA account numbers, journal reference)
/// is never sent to this app — only what a member needs to browse, list and track a sale.
class ShareListing {
  const ShareListing({
    required this.id,
    required this.sellerMemberId,
    required this.sellerSharesAccountNumber,
    required this.amount,
    required this.status,
    required this.listedAt,
    this.buyerMemberId,
    this.buyerSharesAccountNumber,
    this.claimedAt,
    this.decidedAt,
    this.rejectionReason,
  });

  factory ShareListing.fromJson(Map<String, dynamic> json) => ShareListing(
    id: json['id'] as String,
    sellerMemberId: json['sellerMemberId'] as String,
    sellerSharesAccountNumber: json['sellerSharesAccountNumber'] as String,
    amount: (json['amount'] as num).toDouble(),
    status: ShareListingStatus.fromWire(json['status'] as String),
    listedAt: DateTime.parse(json['listedAt'] as String),
    buyerMemberId: json['buyerMemberId'] as String?,
    buyerSharesAccountNumber: json['buyerSharesAccountNumber'] as String?,
    claimedAt: json['claimedAt'] == null ? null : DateTime.parse(json['claimedAt'] as String),
    decidedAt: json['decidedAt'] == null ? null : DateTime.parse(json['decidedAt'] as String),
    rejectionReason: json['rejectionReason'] as String?,
  );

  final String id;
  final String sellerMemberId;
  final String sellerSharesAccountNumber;
  final double amount;
  final ShareListingStatus status;
  final DateTime listedAt;
  final String? buyerMemberId;
  final String? buyerSharesAccountNumber;
  final DateTime? claimedAt;
  final DateTime? decidedAt;
  final String? rejectionReason;
}
