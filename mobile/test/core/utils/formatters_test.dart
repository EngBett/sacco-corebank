import 'package:flutter_test/flutter_test.dart';
import 'package:sacco_member/core/utils/formatters.dart';

void main() {
  test('money formats with the KES prefix and two decimals', () {
    expect(Formatters.money(1500), 'KES 1,500.00');
    expect(Formatters.money(0), 'KES 0.00');
  });

  test('date formats as day month year', () {
    expect(Formatters.date(DateTime(2025, 3, 7)), '7 Mar 2025');
  });
}
