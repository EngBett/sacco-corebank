import '../../../../core/models/enums.dart';

class MyDividend {
  const MyDividend({
    required this.financialYear,
    required this.status,
    required this.shareDividend,
    required this.depositInterest,
    required this.withholdingTax,
    required this.netPayable,
    required this.isPaid,
    this.paidAt,
  });

  factory MyDividend.fromJson(Map<String, dynamic> json) => MyDividend(
    financialYear: json['financialYear'] as int,
    status: DividendStatus.fromWire(json['status'] as String),
    shareDividend: (json['shareDividend'] as num).toDouble(),
    depositInterest: (json['depositInterest'] as num).toDouble(),
    withholdingTax: (json['withholdingTax'] as num).toDouble(),
    netPayable: (json['netPayable'] as num).toDouble(),
    isPaid: json['isPaid'] as bool,
    paidAt: json['paidAt'] == null ? null : DateTime.parse(json['paidAt'] as String),
  );

  final int financialYear;
  final DividendStatus status;
  final double shareDividend;
  final double depositInterest;
  final double withholdingTax;
  final double netPayable;
  final bool isPaid;
  final DateTime? paidAt;

  double get grossDividend => shareDividend + depositInterest;
}
