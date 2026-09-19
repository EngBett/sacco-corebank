import 'account_statement.dart';

class BalancePoint {
  const BalancePoint(this.month, this.balance);

  /// First day of the month this point closes.
  final DateTime month;

  /// Balance at the end of that month (or today, for the current month).
  final double balance;
}

/// Month-end balances derived from a real account statement — the wallet chart plots these, never sample data.
class BalanceTrend {
  const BalanceTrend(this.points);

  final List<BalancePoint> points;

  /// [statement] must cover at least [months] months ending [today]. Lines are in posting order; the last line on or
  /// before a month's end carries that month's closing balance, and a month with no earlier line closes at the
  /// statement's opening balance.
  factory BalanceTrend.fromStatement(AccountStatement statement, DateTime today, {int months = 6}) {
    final points = <BalancePoint>[];
    for (var i = months - 1; i >= 0; i--) {
      final month = DateTime(today.year, today.month - i);
      final monthEnd = DateTime(month.year, month.month + 1).subtract(const Duration(days: 1));
      var balance = statement.openingBalance;
      for (final line in statement.lines) {
        if (!line.valueDate.isAfter(monthEnd)) balance = line.runningBalance;
      }
      points.add(BalancePoint(month, balance));
    }
    return BalanceTrend(points);
  }

  bool get isFlat => points.every((p) => p.balance == points.first.balance);
  double get min => points.map((p) => p.balance).reduce((a, b) => a < b ? a : b);
  double get max => points.map((p) => p.balance).reduce((a, b) => a > b ? a : b);

  /// Change over the window, e.g. +12.5 for 12.5% growth; null when the first month closed at zero.
  double? get changePercent {
    final first = points.first.balance;
    if (first == 0) return null;
    return (points.last.balance - first) / first.abs() * 100;
  }
}
