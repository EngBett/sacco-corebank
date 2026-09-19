import '../../domain/entities/account_statement.dart';
import '../../domain/entities/fees.dart';
import '../../domain/entities/member_profile.dart';
import '../../domain/entities/member_summary.dart';
import '../../domain/entities/savings_account.dart';
import '../../domain/repositories/accounts_repository.dart';
import '../datasources/accounts_remote_datasource.dart';

class AccountsRepositoryImpl implements AccountsRepository {
  AccountsRepositoryImpl(this._remote);
  final AccountsRemoteDataSource _remote;

  @override
  Future<List<SavingsAccount>> getAccounts() async =>
      (await _remote.getAccounts()).map(SavingsAccount.fromJson).toList();

  @override
  Future<MemberSavingsSummary> getSummary() async => MemberSavingsSummary.fromJson(await _remote.getSummary());

  @override
  Future<MemberProfile> getProfile() async => MemberProfile.fromJson(await _remote.getProfile());

  @override
  Future<AccountStatement> getStatement(String accountNumber, {DateTime? from, DateTime? to}) async =>
      AccountStatement.fromJson(await _remote.getStatement(accountNumber, from: from, to: to));

  @override
  Future<BalanceReveal> revealBalance(String accountNumber, String idempotencyKey) async =>
      BalanceReveal.fromJson(await _remote.revealBalance(accountNumber, idempotencyKey));

  @override
  Future<FeeQuote> quoteFee({
    required FeeTransactionType type,
    required String accountNumber,
    required String channel,
    required double amount,
  }) async => FeeQuote.fromJson(
    await _remote.quoteFee(
      transactionType: type.wireName,
      accountNumber: accountNumber,
      channel: channel,
      amount: amount,
    ),
  );

  @override
  Future<PinCheckResult> verifyPin(String pin) async {
    final json = await _remote.verifyPin(pin);
    if (json['valid'] as bool) return PinCheckResult.valid;
    return (json['lockedOut'] as bool) ? PinCheckResult.lockedOut : PinCheckResult.invalid;
  }
}
