import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:iconsax_flutter/iconsax_flutter.dart';

import '../../../../core/models/enums.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../core/utils/formatters.dart';
import '../../domain/entities/account_statement.dart';
import '../providers/balance_privacy.dart';

/// Statement lines newest first, grouped under day headers with that day's net movement — the expenses reference's
/// "Today · $1,145.00" layout. [limit] caps the number of lines (for the dashboard preview).
List<Widget> buildTransactionGroups(BuildContext context, List<StatementLine> lines, {int? limit}) {
  final newest = lines.reversed.take(limit ?? lines.length).toList();
  final widgets = <Widget>[];
  DateTime? day;
  var dayLines = <StatementLine>[];

  void flush() {
    if (day == null) return;
    widgets.add(_DayHeader(day: day, lines: dayLines));
    for (final line in dayLines) {
      widgets.add(
        Padding(
          padding: const EdgeInsets.only(bottom: 10),
          child: TransactionTile(line: line),
        ),
      );
    }
  }

  for (final line in newest) {
    final d = DateUtils.dateOnly(line.valueDate);
    if (day != d) {
      flush();
      day = d;
      dayLines = [];
    }
    dayLines.add(line);
  }
  flush();
  return widgets;
}

String dayLabel(DateTime day) {
  final today = DateUtils.dateOnly(DateTime.now());
  if (day == today) return 'Today';
  if (day == today.subtract(const Duration(days: 1))) return 'Yesterday';
  return Formatters.date(day);
}

class _DayHeader extends ConsumerWidget {
  const _DayHeader({required this.day, required this.lines});
  final DateTime day;
  final List<StatementLine> lines;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final hidden = ref.watch(balancePrivacyProvider).hidden;
    final net = lines.fold<double>(0, (sum, l) => sum + (l.direction == EntryDirection.credit ? l.amount : -l.amount));
    return Padding(
      padding: const EdgeInsets.only(top: 8, bottom: 12),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Semantics(
            header: true,
            child: Text(
              dayLabel(day),
              style: const TextStyle(color: AppTheme.textSecondary, fontSize: 14, fontWeight: FontWeight.w700),
            ),
          ),
          // A one-line day's total would just repeat that line's amount.
          if (lines.length > 1)
            Text(
              hidden ? '••••' : '${net >= 0 ? '+' : '−'}${Formatters.money(net.abs())}',
              style: AppTheme.money(size: 14, color: AppTheme.textSecondary),
            ),
        ],
      ),
    );
  }
}

class TransactionTile extends ConsumerWidget {
  const TransactionTile({super.key, required this.line});
  final StatementLine line;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final hidden = ref.watch(balancePrivacyProvider).hidden;
    final credit = line.direction == EntryDirection.credit;
    final tint = credit ? AppTheme.positive : AppTheme.textSecondary;
    final title = (line.narrative?.isNotEmpty ?? false) ? line.narrative! : line.description;
    final amount = '${credit ? '+' : '−'}${Formatters.money(line.amount)}';

    return Semantics(
      label:
          '${credit ? 'Money in' : 'Money out'}: $title, ${hidden ? 'amount hidden' : amount}, ${dayLabel(DateUtils.dateOnly(line.valueDate))}',
      excludeSemantics: true,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
        decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(AppTheme.radiusMd)),
        child: Row(
          children: [
            Container(
              width: 44,
              height: 44,
              decoration: BoxDecoration(color: tint.withValues(alpha: 0.12), borderRadius: BorderRadius.circular(14)),
              child: Icon(credit ? Iconsax.arrow_down_copy : Iconsax.arrow_up_3_copy, color: tint, size: 20),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    title,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(color: AppTheme.textPrimary, fontWeight: FontWeight.w600, fontSize: 15),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    line.reference,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(color: AppTheme.textSecondary, fontSize: 12),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 12),
            Text(
              hidden ? '••••' : amount,
              style: AppTheme.money(size: 15, color: credit ? AppTheme.positive : AppTheme.textPrimary),
            ),
          ],
        ),
      ),
    );
  }
}
