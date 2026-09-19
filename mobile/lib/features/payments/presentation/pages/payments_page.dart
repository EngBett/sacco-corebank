import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../../core/models/enums.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../core/utils/formatters.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../domain/entities/payment_transaction.dart';
import '../../domain/entities/withdrawal.dart';
import '../providers/payments_providers.dart';

class PaymentsPage extends ConsumerWidget {
  const PaymentsPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return DefaultTabController(
      length: 2,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('Payments'),
          bottom: TabBar(
            labelColor: AppTheme.textPrimary,
            unselectedLabelColor: AppTheme.textSecondary,
            indicatorColor: Theme.of(context).colorScheme.primary,
            tabs: const [
              Tab(text: 'Withdrawals'),
              Tab(text: 'Deposits'),
            ],
          ),
        ),
        floatingActionButton: FloatingActionButton.extended(
          onPressed: () => context.push('/withdraw'),
          icon: const Icon(Icons.remove_circle_outline),
          label: const Text('Withdraw'),
        ),
        body: TabBarView(children: [_WithdrawalsTab(), _DepositsTab()]),
      ),
    );
  }
}

class _WithdrawalsTab extends ConsumerWidget {
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final withdrawals = ref.watch(myWithdrawalsProvider);
    return AsyncValueView(
      value: withdrawals,
      onRetry: () => ref.invalidate(myWithdrawalsProvider),
      data: (context, list) {
        if (list.isEmpty) {
          return const Center(
            child: Text("You haven't requested any withdrawals yet.", style: TextStyle(color: AppTheme.textSecondary)),
          );
        }
        return ListView.builder(
          padding: const EdgeInsets.all(16),
          itemCount: list.length,
          itemBuilder: (context, index) => _WithdrawalTile(withdrawal: list[index]),
        );
      },
    );
  }
}

class _WithdrawalTile extends StatelessWidget {
  const _WithdrawalTile({required this.withdrawal});
  final Withdrawal withdrawal;

  Color _statusColor() => switch (withdrawal.status) {
    WithdrawalStatus.paid => AppTheme.positive,
    WithdrawalStatus.approved => const Color(0xFF6C8CFF),
    WithdrawalStatus.pendingApproval => const Color(0xFFFFB86C),
    WithdrawalStatus.rejected || WithdrawalStatus.cancelled => AppTheme.negative,
  };

  @override
  Widget build(BuildContext context) {
    final color = _statusColor();
    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: ListTile(
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
        leading: CircleAvatar(
          radius: 22,
          backgroundColor: color.withValues(alpha: 0.18),
          child: Icon(Icons.arrow_upward_rounded, color: color),
        ),
        title: Text(
          Formatters.money(withdrawal.amount),
          style: const TextStyle(color: AppTheme.textPrimary, fontWeight: FontWeight.w700),
        ),
        subtitle: Text(
          '${withdrawal.channel.label} • ${withdrawal.accountNumber}\n${Formatters.dateTime(withdrawal.requestedAt)}',
          style: const TextStyle(color: AppTheme.textSecondary),
        ),
        isThreeLine: true,
        trailing: Chip(
          label: Text(withdrawal.status.label, style: const TextStyle(color: Colors.white, fontSize: 12)),
          backgroundColor: color,
          side: BorderSide.none,
          padding: EdgeInsets.zero,
        ),
      ),
    );
  }
}

class _DepositsTab extends ConsumerWidget {
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final payments = ref.watch(myPaymentsProvider);
    return AsyncValueView(
      value: payments,
      onRetry: () => ref.invalidate(myPaymentsProvider),
      data: (context, list) {
        if (list.isEmpty) {
          return const Center(
            child: Text("You haven't made any deposits yet.", style: TextStyle(color: AppTheme.textSecondary)),
          );
        }
        return ListView.builder(
          padding: const EdgeInsets.all(16),
          itemCount: list.length,
          itemBuilder: (context, index) => _PaymentTile(payment: list[index]),
        );
      },
    );
  }
}

class _PaymentTile extends StatelessWidget {
  const _PaymentTile({required this.payment});
  final PaymentTransaction payment;

  Color _statusColor() => switch (payment.status) {
    PaymentStatus.succeeded => AppTheme.positive,
    PaymentStatus.pendingCallback || PaymentStatus.initiated => const Color(0xFFFFB86C),
    PaymentStatus.failed || PaymentStatus.timedOut => AppTheme.negative,
  };

  @override
  Widget build(BuildContext context) {
    final color = _statusColor();
    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: ListTile(
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
        leading: CircleAvatar(
          radius: 22,
          backgroundColor: color.withValues(alpha: 0.18),
          child: Icon(Icons.arrow_downward_rounded, color: color),
        ),
        title: Text(
          Formatters.money(payment.amount),
          style: const TextStyle(color: AppTheme.textPrimary, fontWeight: FontWeight.w700),
        ),
        subtitle: Text(
          '${payment.provider} • ${payment.accountNumber ?? '-'}\n${Formatters.dateTime(payment.initiatedAt)}',
          style: const TextStyle(color: AppTheme.textSecondary),
        ),
        isThreeLine: true,
        trailing: Chip(
          label: Text(payment.status.label, style: const TextStyle(color: Colors.white, fontSize: 12)),
          backgroundColor: color,
          side: BorderSide.none,
          padding: EdgeInsets.zero,
        ),
      ),
    );
  }
}
