import 'package:flutter_test/flutter_test.dart';
import 'package:sacco_member/core/models/enums.dart';
import 'package:sacco_member/features/accounts/domain/entities/account_statement.dart';
import 'package:sacco_member/features/accounts/domain/entities/balance_trend.dart';

StatementLine _line(DateTime date, double amount, double running) => StatementLine(
  valueDate: date,
  reference: 'REF',
  description: 'test',
  direction: amount >= 0 ? EntryDirection.credit : EntryDirection.debit,
  amount: amount.abs(),
  runningBalance: running,
);

void main() {
  final today = DateTime(2026, 9, 17);

  test('each month closes at its last posted running balance, carrying forward quiet months', () {
    final statement = AccountStatement(
      accountNumber: 'M00003-FO',
      openingBalance: 1000,
      closingBalance: 4000,
      lines: [
        _line(DateTime(2026, 5, 3), 500, 1500),
        _line(DateTime(2026, 5, 28), 1000, 2500),
        _line(DateTime(2026, 7, 31), 2000, 4500),
        _line(DateTime(2026, 9, 1), -500, 4000),
      ],
    );

    final trend = BalanceTrend.fromStatement(statement, today);

    expect(trend.points.map((p) => p.month.month), [4, 5, 6, 7, 8, 9]);
    expect(trend.points.map((p) => p.balance), [1000, 2500, 2500, 4500, 4500, 4000]);
    expect(trend.changePercent, 300);
  });

  test('an account with no movement is flat at its opening balance', () {
    const statement = AccountStatement(accountNumber: 'X', openingBalance: 20000, closingBalance: 20000, lines: []);
    final trend = BalanceTrend.fromStatement(statement, today);
    expect(trend.isFlat, isTrue);
    expect(trend.points.every((p) => p.balance == 20000), isTrue);
  });
}
