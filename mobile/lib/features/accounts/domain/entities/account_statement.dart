import '../../../../core/models/enums.dart';

class StatementLine {
  const StatementLine({
    required this.valueDate,
    required this.reference,
    required this.description,
    required this.direction,
    required this.amount,
    required this.runningBalance,
    this.narrative,
  });

  factory StatementLine.fromJson(Map<String, dynamic> json) => StatementLine(
    valueDate: DateTime.parse(json['valueDate'] as String),
    reference: json['reference'] as String,
    description: json['description'] as String,
    narrative: json['narrative'] as String?,
    direction: EntryDirection.fromWire(json['direction'] as String),
    amount: (json['amount'] as num).toDouble(),
    runningBalance: (json['runningBalance'] as num).toDouble(),
  );

  final DateTime valueDate;
  final String reference;
  final String description;
  final String? narrative;
  final EntryDirection direction;
  final double amount;
  final double runningBalance;
}

class AccountStatement {
  const AccountStatement({
    required this.accountNumber,
    required this.openingBalance,
    required this.closingBalance,
    required this.lines,
  });

  factory AccountStatement.fromJson(Map<String, dynamic> json) => AccountStatement(
    accountNumber: json['accountNumber'] as String,
    openingBalance: (json['openingBalance'] as num).toDouble(),
    closingBalance: (json['closingBalance'] as num).toDouble(),
    lines: (json['lines'] as List<dynamic>).map((e) => StatementLine.fromJson(e as Map<String, dynamic>)).toList(),
  );

  final String accountNumber;
  final double openingBalance;
  final double closingBalance;
  final List<StatementLine> lines;
}
