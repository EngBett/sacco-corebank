import '../../../../core/models/enums.dart';
import '../entities/payment_transaction.dart';
import '../entities/withdrawal.dart';

abstract class PaymentsRepository {
  /// Mobile-money/bank push to the member's own phone into their own account or loan
  /// (`/api/self/payments/topup`). NCBA never appears here — it is payout-only.
  Future<PaymentTransaction> topUp({
    required String provider,
    required double amount,
    required String accountNumber,
    String? loanNumber,
  });

  Future<List<PaymentTransaction>> getMyPayments();

  /// Always lands `PendingApproval` — a staff checker must approve before payout
  /// (maker-checker, ADR 0008/0014). The channel is one of cash/M-Pesa/Airtel
  /// Money/bank Pesalink; NCBA-vs-Equity is never a member-facing choice.
  Future<Withdrawal> requestWithdrawal({
    required String accountNumber,
    required double amount,
    required PayoutChannel channel,
    String? destination,
    String? narrative,
  });

  Future<List<Withdrawal>> getMyWithdrawals();
}
