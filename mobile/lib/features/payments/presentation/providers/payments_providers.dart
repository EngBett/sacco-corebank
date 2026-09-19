import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/di/service_locator.dart';
import '../../domain/entities/withdrawal.dart';
import '../../domain/repositories/payments_repository.dart';

final paymentsRepositoryProvider = Provider<PaymentsRepository>((ref) => sl<PaymentsRepository>());

final myWithdrawalsProvider = FutureProvider.autoDispose<List<Withdrawal>>(
  (ref) => ref.watch(paymentsRepositoryProvider).getMyWithdrawals(),
);

final myPaymentsProvider = FutureProvider.autoDispose((ref) => ref.watch(paymentsRepositoryProvider).getMyPayments());
