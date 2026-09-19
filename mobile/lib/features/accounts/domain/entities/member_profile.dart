import '../../../../core/models/enums.dart';

class NextOfKin {
  const NextOfKin({required this.name, required this.relationship, required this.phoneNumber});

  factory NextOfKin.fromJson(Map<String, dynamic> json) => NextOfKin(
    name: json['name'] as String,
    relationship: json['relationship'] as String,
    phoneNumber: json['phoneNumber'] as String,
  );

  final String name;
  final String relationship;
  final String phoneNumber;
}

class MemberProfile {
  const MemberProfile({
    required this.id,
    required this.memberNumber,
    required this.fullName,
    required this.phoneNumber,
    required this.kycStatus,
    required this.joinedAt,
    required this.nextOfKin,
    this.email,
  });

  factory MemberProfile.fromJson(Map<String, dynamic> json) => MemberProfile(
    id: json['id'] as String,
    memberNumber: json['memberNumber'] as String,
    fullName: json['fullName'] as String,
    phoneNumber: json['phoneNumber'] as String,
    email: json['email'] as String?,
    kycStatus: KycStatus.fromWire(json['kycStatus'] as String),
    joinedAt: DateTime.parse(json['joinedAt'] as String),
    nextOfKin: NextOfKin.fromJson(json['nextOfKin'] as Map<String, dynamic>),
  );

  final String id;
  final String memberNumber;
  final String fullName;
  final String phoneNumber;
  final String? email;
  final KycStatus kycStatus;
  final DateTime joinedAt;
  final NextOfKin nextOfKin;
}
