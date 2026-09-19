import '../../../../core/models/enums.dart';
import '../../domain/entities/payment_transaction.dart';
import '../../domain/entities/withdrawal.dart';
import '../../domain/repositories/payments_repository.dart';
import '../datasources/payments_remote_datasource.dart';

class PaymentsRepositoryImpl implements PaymentsRepository {
  PaymentsRepositoryImpl(this._remote);
  final PaymentsRemoteDataSource _remote;

  @override
  Future<PaymentTransaction> topUp({
    required String provider,
    required double amount,
    required String accountNumber,
    String? loanNumber,
  }) async {
    final json = await _remote.topUp(
      provider: provider,
      amount: amount,
      accountNumber: accountNumber,
      loanNumber: loanNumber,
    );
    return PaymentTransaction.fromJson(json);
  }

  @override
  Future<List<PaymentTransaction>> getMyPayments() async =>
      (await _remote.getMyPayments()).map(PaymentTransaction.fromJson).toList();

  @override
  Future<Withdrawal> requestWithdrawal({
    required String accountNumber,
    required double amount,
    required PayoutChannel channel,
    String? destination,
    String? narrative,
  }) async {
    final json = await _remote.requestWithdrawal(
      accountNumber: accountNumber,
      amount: amount,
      channelWireName: channel.wireName,
      destination: destination,
      narrative: narrative,
    );
    return Withdrawal.fromJson(json);
  }

  @override
  Future<List<Withdrawal>> getMyWithdrawals() async =>
      (await _remote.getMyWithdrawals()).map(Withdrawal.fromJson).toList();
}
