import '../entities/account_statement.dart';
import '../entities/fees.dart';
import '../entities/member_profile.dart';
import '../entities/member_summary.dart';
import '../entities/savings_account.dart';

abstract class AccountsRepository {
  Future<List<SavingsAccount>> getAccounts();
  Future<MemberSavingsSummary> getSummary();
  Future<MemberProfile> getProfile();
  Future<AccountStatement> getStatement(String accountNumber, {DateTime? from, DateTime? to});

  /// Charges the product's balance-enquiry fee to the member's FOSA account and opens a short reveal window.
  /// Retrying with the same [idempotencyKey] never charges twice.
  Future<BalanceReveal> revealBalance(String accountNumber, String idempotencyKey);

  /// [channel] is a fee-matrix channel wire name: Cash, MPesa, AirtelMoney, BankTransfer.
  Future<FeeQuote> quoteFee({
    required FeeTransactionType type,
    required String accountNumber,
    required String channel,
    required double amount,
  });

  /// Re-checks the signed-in member's PIN; wrong PINs count toward the sign-in lockout.
  Future<PinCheckResult> verifyPin(String pin);
}
