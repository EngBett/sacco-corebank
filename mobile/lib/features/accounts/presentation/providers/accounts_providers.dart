import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/di/service_locator.dart';
import '../../domain/entities/account_statement.dart';
import '../../domain/entities/fees.dart';
import '../../domain/entities/member_profile.dart';
import '../../domain/entities/member_summary.dart';
import '../../domain/entities/savings_account.dart';
import '../../domain/repositories/accounts_repository.dart';

final accountsRepositoryProvider = Provider<AccountsRepository>((ref) => sl<AccountsRepository>());

final myProfileProvider = FutureProvider<MemberProfile>((ref) => ref.watch(accountsRepositoryProvider).getProfile());

final mySummaryProvider = FutureProvider<MemberSavingsSummary>(
  (ref) => ref.watch(accountsRepositoryProvider).getSummary(),
);

final myAccountsProvider = FutureProvider<List<SavingsAccount>>(
  (ref) => ref.watch(accountsRepositoryProvider).getAccounts(),
);

final accountStatementProvider = FutureProvider.family<AccountStatement, String>(
  (ref, accountNumber) => ref.watch(accountsRepositoryProvider).getStatement(accountNumber),
);

/// (type, account number, fee-matrix channel wire name, amount) → what that transaction would cost (ADR 0015).
final feeQuoteProvider = FutureProvider.autoDispose.family<FeeQuote, (FeeTransactionType, String, String, double)>(
  (ref, q) =>
      ref.watch(accountsRepositoryProvider).quoteFee(type: q.$1, accountNumber: q.$2, channel: q.$3, amount: q.$4),
);

/// How many months of history the wallet chart and activity list cover.
const historyMonths = 6;

/// Six months of statement for one account — feeds the balance chart and the recent-activity list. A fee-gated
/// account fails with `savings.balance.locked` until its balance is revealed (ADR 0015).
final accountHistoryProvider = FutureProvider.autoDispose.family<AccountStatement, String>((ref, accountNumber) {
  final now = DateTime.now();
  return ref
      .watch(accountsRepositoryProvider)
      .getStatement(accountNumber, from: DateTime(now.year, now.month - (historyMonths - 1)), to: now);
});
