import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/models/enums.dart';
import '../../../../core/utils/formatters.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../domain/entities/dividend.dart';
import '../providers/dividends_providers.dart';

class DividendsPage extends ConsumerWidget {
  const DividendsPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final dividends = ref.watch(myDividendsProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Dividends')),
      body: AsyncValueView(
        value: dividends,
        onRetry: () => ref.invalidate(myDividendsProvider),
        data: (context, list) {
          if (list.isEmpty) {
            return const Center(
              child: Padding(
                padding: EdgeInsets.all(24),
                child: Text(
                  'No dividends have been declared for you yet. They appear here as soon as your SACCO '
                  'declares one for a financial year.',
                  textAlign: TextAlign.center,
                ),
              ),
            );
          }
          return RefreshIndicator(
            onRefresh: () async => ref.invalidate(myDividendsProvider),
            child: ListView.builder(
              padding: const EdgeInsets.all(16),
              itemCount: list.length,
              itemBuilder: (context, index) => _DividendCard(dividend: list[index]),
            ),
          );
        },
      ),
    );
  }
}

class _DividendCard extends StatelessWidget {
  const _DividendCard({required this.dividend});
  final MyDividend dividend;

  Color _statusColor() => switch (dividend.status) {
    DividendStatus.paid => Colors.green,
    DividendStatus.approved => Colors.blue,
    DividendStatus.declared => Colors.orange,
    DividendStatus.rejected => Colors.red,
  };

  @override
  Widget build(BuildContext context) {
    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text('FY ${dividend.financialYear}', style: Theme.of(context).textTheme.titleMedium),
                Chip(
                  label: Text(dividend.status.label, style: const TextStyle(color: Colors.white, fontSize: 12)),
                  backgroundColor: _statusColor(),
                  padding: EdgeInsets.zero,
                ),
              ],
            ),
            const SizedBox(height: 12),
            Text(Formatters.money(dividend.netPayable), style: Theme.of(context).textTheme.headlineSmall),
            Text('Net payable', style: Theme.of(context).textTheme.bodySmall),
            const Divider(height: 24),
            _row(context, 'Share dividend', dividend.shareDividend),
            _row(context, 'Deposit interest', dividend.depositInterest),
            _row(context, 'Withholding tax', -dividend.withholdingTax),
            if (dividend.isPaid && dividend.paidAt != null) ...[
              const SizedBox(height: 8),
              Text('Paid on ${Formatters.date(dividend.paidAt!)}', style: Theme.of(context).textTheme.bodySmall),
            ],
          ],
        ),
      ),
    );
  }

  Widget _row(BuildContext context, String label, double amount) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 2),
    child: Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Text(label, style: Theme.of(context).textTheme.bodyMedium),
        Text(Formatters.money(amount)),
      ],
    ),
  );
}
