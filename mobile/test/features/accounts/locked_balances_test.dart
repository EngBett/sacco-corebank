import 'package:flutter_test/flutter_test.dart';
import 'package:sacco_member/features/accounts/domain/entities/member_summary.dart';
import 'package:sacco_member/features/accounts/domain/entities/savings_account.dart';

void main() {
  test('a fee-locked account parses with null balances and its enquiry fee', () {
    final account = SavingsAccount.fromJson({
      'id': 'a1',
      'accountNumber': 'M00006-FO',
      'memberId': 'm1',
      'productCode': 'FOSA-CUR',
      'kind': 'FosaCurrent',
      'status': 'Active',
      'openedAt': '2025-08-17T00:00:00Z',
      'balance': null,
      'heldAmount': null,
      'availableBalance': null,
      'balanceLocked': true,
      'balanceEnquiryFee': 10,
      'balanceVisibleUntil': null,
    });
    expect(account.balanceLocked, isTrue);
    expect(account.balance, isNull);
    expect(account.balanceEnquiryFee, 10);
  });

  test('the dashboard total only adds the buckets the member can see', () {
    final summary = MemberSavingsSummary.fromJson({
      'memberId': 'm1',
      'bosaDeposits': 33000,
      'shares': 15000,
      'fosaBalance': null,
      'fixedDeposits': 0,
      'monthsWithContributions': 11,
      'hasLockedBalances': true,
    });
    expect(summary.visibleTotal, 48000);
    expect(summary.hasLockedBalances, isTrue);
  });
}
