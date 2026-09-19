import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:iconsax_flutter/iconsax_flutter.dart';

import '../../../../core/models/enums.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../../../core/widgets/motion.dart';
import '../../../../core/widgets/skeleton.dart';
import '../../domain/entities/account_statement.dart';
import '../../domain/entities/savings_account.dart';
import '../providers/accounts_providers.dart';
import '../widgets/account_visuals.dart';
import '../widgets/balance_reveal.dart';
import '../widgets/balance_trend_chart.dart';
import '../widgets/transaction_list.dart';

/// One account's statement in the expenses reference layout: a rounded header with the closing balance and the
/// six-month balance chart, then every transaction grouped by day.
class StatementPage extends ConsumerWidget {
  const StatementPage({super.key, required this.accountNumber});
  final String accountNumber;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final history = ref.watch(accountHistoryProvider(accountNumber));
    final account = ref
        .watch(myAccountsProvider)
        .valueOrNull
        ?.where((a) => a.accountNumber == accountNumber)
        .firstOrNull;

    return Scaffold(
      body: AsyncValueView(
        value: history,
        onRetry: () => ref.invalidate(accountHistoryProvider(accountNumber)),
        loading: const _StatementSkeleton(),
        data: (context, statement) => RefreshIndicator(
          onRefresh: () async {
            ref.invalidate(accountHistoryProvider(accountNumber));
            ref.invalidate(myAccountsProvider);
          },
          child: CustomScrollView(
            slivers: [
              _StatementHeader(statement: statement, account: account),
              if (statement.lines.isEmpty)
                const SliverFillRemaining(
                  hasScrollBody: false,
                  child: Center(
                    child: Padding(
                      padding: EdgeInsets.all(32),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Icon(Iconsax.receipt_item_copy, size: 36, color: AppTheme.textSecondary),
                          SizedBox(height: 12),
                          Text(
                            'No transactions in the last six months.',
                            textAlign: TextAlign.center,
                            style: TextStyle(color: AppTheme.textSecondary),
                          ),
                        ],
                      ),
                    ),
                  ),
                )
              else
                SliverPadding(
                  padding: const EdgeInsets.fromLTRB(20, 24, 20, 40),
                  sliver: SliverList.list(
                    children: [
                      for (final (i, w) in buildTransactionGroups(context, statement.lines).indexed)
                        FadeSlideIn(delay: FadeSlideIn.step * i.clamp(0, 8), child: w),
                    ],
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _StatementHeader extends ConsumerWidget {
  const _StatementHeader({required this.statement, required this.account});
  final AccountStatement statement;
  final SavingsAccount? account;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final topInset = MediaQuery.paddingOf(context).top;
    final kind = account?.kind;
    final credits = statement.lines
        .where((l) => l.direction == EntryDirection.credit)
        .fold<double>(0, (s, l) => s + l.amount);
    final debits = statement.lines
        .where((l) => l.direction == EntryDirection.debit)
        .fold<double>(0, (s, l) => s + l.amount);

    return SliverAppBar(
      pinned: true,
      expandedHeight: 470,
      toolbarHeight: 64,
      backgroundColor: AppTheme.surface,
      surfaceTintColor: Colors.transparent,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(bottom: Radius.circular(AppTheme.radiusXl)),
      ),
      title: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(kind?.label ?? 'Statement', style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
          Text(
            statement.accountNumber,
            style: const TextStyle(color: AppTheme.textSecondary, fontSize: 12, fontFeatures: AppTheme.moneyFigures),
          ),
        ],
      ),
      flexibleSpace: FlexibleSpaceBar(
        collapseMode: CollapseMode.pin,
        background: FadeOnCollapse(
          child: Padding(
            padding: EdgeInsets.fromLTRB(20, topInset + 64 + 16, 20, 16),
            child: Column(
              children: [
                if (kind != null)
                  Container(
                    width: 48,
                    height: 48,
                    decoration: BoxDecoration(
                      gradient: LinearGradient(colors: AccountVisuals.gradient(kind)),
                      borderRadius: BorderRadius.circular(16),
                    ),
                    child: Icon(AccountVisuals.icon(kind), color: Colors.white, size: 22),
                  ),
                const SizedBox(height: 12),
                const Text('Closing balance', style: TextStyle(color: AppTheme.textSecondary, fontSize: 13)),
                const SizedBox(height: 6),
                FittedBox(
                  fit: BoxFit.scaleDown,
                  child: BalanceText(
                    statement.closingBalance,
                    style: AppTheme.money(size: 34, weight: FontWeight.w800),
                  ),
                ),
                const SizedBox(height: 14),
                Row(
                  children: [
                    Expanded(
                      child: _Flow(
                        icon: Iconsax.arrow_down_copy,
                        label: 'Money in',
                        amount: credits,
                        color: AppTheme.positive,
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: _Flow(
                        icon: Iconsax.arrow_up_3_copy,
                        label: 'Money out',
                        amount: debits,
                        color: AppTheme.textSecondary,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 18),
                Expanded(
                  child: BalanceTrendChart(statement: statement, label: kind?.label),
                ),
                const SizedBox(height: 10),
                Container(
                  width: 36,
                  height: 4,
                  decoration: BoxDecoration(color: AppTheme.border, borderRadius: BorderRadius.circular(4)),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _Flow extends StatelessWidget {
  const _Flow({required this.icon, required this.label, required this.amount, required this.color});
  final IconData icon;
  final String label;
  final double amount;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
      decoration: BoxDecoration(color: AppTheme.surfaceRaised, borderRadius: BorderRadius.circular(AppTheme.radiusMd)),
      child: Row(
        children: [
          Icon(icon, size: 18, color: color),
          const SizedBox(width: 8),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(label, style: const TextStyle(color: AppTheme.textSecondary, fontSize: 11)),
                FittedBox(
                  fit: BoxFit.scaleDown,
                  alignment: Alignment.centerLeft,
                  child: BalanceText(amount, style: AppTheme.money(size: 14)),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _StatementSkeleton extends StatelessWidget {
  const _StatementSkeleton();

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          children: [
            const Align(alignment: Alignment.centerLeft, child: BackButton()),
            const SizedBox(height: 24),
            const Skeleton(width: 180, height: 40),
            const SizedBox(height: 24),
            const Skeleton(height: 200, radius: AppTheme.radiusLg),
            const SizedBox(height: 24),
            for (var i = 0; i < 4; i++)
              const Padding(padding: EdgeInsets.only(bottom: 10), child: Skeleton(height: 68)),
          ],
        ),
      ),
    );
  }
}
