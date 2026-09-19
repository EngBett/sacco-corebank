import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/models/enums.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../core/utils/formatters.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../../accounts/presentation/providers/accounts_providers.dart';
import '../../domain/entities/share_listing.dart';
import '../providers/share_marketplace_providers.dart';

class ShareMarketplacePage extends ConsumerWidget {
  const ShareMarketplacePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return DefaultTabController(
      length: 2,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('Shares marketplace'),
          bottom: TabBar(
            labelColor: AppTheme.textPrimary,
            unselectedLabelColor: AppTheme.textSecondary,
            indicatorColor: Theme.of(context).colorScheme.primary,
            tabs: const [
              Tab(text: 'Browse'),
              Tab(text: 'My activity'),
            ],
          ),
        ),
        floatingActionButton: FloatingActionButton.extended(
          onPressed: () => _showListDialog(context, ref),
          icon: const Icon(Icons.sell_outlined),
          label: const Text('Sell shares'),
        ),
        body: const TabBarView(children: [_BrowseTab(), _MyActivityTab()]),
      ),
    );
  }

  Future<void> _showListDialog(BuildContext context, WidgetRef ref) async {
    final accounts = await ref.read(myAccountsProvider.future);
    final sharesAccounts = accounts.where((a) => a.kind == ProductKind.shares);
    final shares = sharesAccounts.isEmpty ? null : sharesAccounts.first;
    if (!context.mounted) return;
    if (shares == null) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text("You don't have a shares account yet.")));
      return;
    }
    await showDialog<void>(
      context: context,
      builder: (context) => _ListSharesDialog(availableBalance: shares.availableBalance),
    );
  }
}

class _ListSharesDialog extends ConsumerStatefulWidget {
  const _ListSharesDialog({required this.availableBalance});

  /// Null when the shares balance is hidden behind a balance-enquiry fee; the server still enforces the limit.
  final double? availableBalance;

  @override
  ConsumerState<_ListSharesDialog> createState() => _ListSharesDialogState();
}

class _ListSharesDialogState extends ConsumerState<_ListSharesDialog> {
  final _formKey = GlobalKey<FormState>();
  final _amountController = TextEditingController();
  bool _submitting = false;
  String? _error;

  @override
  void dispose() {
    _amountController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      await ref.read(shareMarketplaceRepositoryProvider).listSharesForSale(double.parse(_amountController.text));
      ref.invalidate(myShareListingsProvider);
      ref.invalidate(myAccountsProvider);
      if (!mounted) return;
      Navigator.of(context).pop();
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Your shares are now listed for sale.')));
    } catch (_) {
      if (mounted) setState(() => _error = "Couldn't list your shares. Check the amount and try again.");
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('Sell shares'),
      content: Form(
        key: _formKey,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${widget.availableBalance == null ? '' : 'Your shares available balance is ${Formatters.money(widget.availableBalance!)}. '}'
              'Listed shares are sold to another member at this exact amount and still need staff approval before anything moves.',
              style: Theme.of(context).textTheme.bodySmall?.copyWith(color: AppTheme.textSecondary),
            ),
            const SizedBox(height: 16),
            TextFormField(
              controller: _amountController,
              autofocus: true,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              decoration: const InputDecoration(labelText: 'Amount to sell (KES)'),
              validator: (value) {
                final amount = double.tryParse(value ?? '');
                if (amount == null || amount <= 0) return 'Enter a valid amount';
                if (widget.availableBalance != null && amount > widget.availableBalance!) {
                  return 'More than your available balance';
                }
                return null;
              },
            ),
            if (_error != null) ...[
              const SizedBox(height: 8),
              Text(_error!, style: const TextStyle(color: Colors.redAccent)),
            ],
          ],
        ),
      ),
      actions: [
        TextButton(onPressed: _submitting ? null : () => Navigator.of(context).pop(), child: const Text('Cancel')),
        FilledButton(
          onPressed: _submitting ? null : _submit,
          child: _submitting
              ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
              : const Text('List for sale'),
        ),
      ],
    );
  }
}

class _BrowseTab extends ConsumerWidget {
  const _BrowseTab();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final listings = ref.watch(openShareListingsProvider);
    return AsyncValueView(
      value: listings,
      onRetry: () => ref.invalidate(openShareListingsProvider),
      data: (context, list) {
        if (list.isEmpty) {
          return const Center(
            child: Padding(
              padding: EdgeInsets.all(24),
              child: Text(
                'No members are selling shares right now. Check back later, or list your own shares for sale.',
                textAlign: TextAlign.center,
                style: TextStyle(color: AppTheme.textSecondary),
              ),
            ),
          );
        }
        return RefreshIndicator(
          onRefresh: () async => ref.invalidate(openShareListingsProvider),
          child: ListView.builder(
            padding: const EdgeInsets.all(16),
            itemCount: list.length,
            itemBuilder: (context, index) => _OpenListingTile(listing: list[index]),
          ),
        );
      },
    );
  }
}

class _OpenListingTile extends ConsumerStatefulWidget {
  const _OpenListingTile({required this.listing});
  final ShareListing listing;

  @override
  ConsumerState<_OpenListingTile> createState() => _OpenListingTileState();
}

class _OpenListingTileState extends ConsumerState<_OpenListingTile> {
  bool _buying = false;

  Future<void> _buy() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Buy these shares?'),
        content: Text(
          '${Formatters.money(widget.listing.amount)} will be held from your FOSA account until a staff member '
          'approves the transfer.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(context).pop(false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.of(context).pop(true), child: const Text('Buy')),
        ],
      ),
    );
    if (confirmed != true) return;
    setState(() => _buying = true);
    try {
      await ref.read(shareMarketplaceRepositoryProvider).claim(widget.listing.id);
      ref.invalidate(openShareListingsProvider);
      ref.invalidate(myShareListingsProvider);
      if (!mounted) return;
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('Purchase requested — awaiting staff approval.')));
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text("Couldn't complete that purchase. Please try again.")));
      }
    } finally {
      if (mounted) setState(() => _buying = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(18)),
      child: Row(
        children: [
          CircleAvatar(
            radius: 22,
            backgroundColor: const Color(0xFFB18CFF).withValues(alpha: 0.18),
            child: const Icon(Icons.pie_chart_outline, color: Color(0xFFB18CFF)),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  Formatters.money(widget.listing.amount),
                  style: const TextStyle(color: AppTheme.textPrimary, fontWeight: FontWeight.w700),
                ),
                Text(
                  'Listed ${Formatters.date(widget.listing.listedAt)}',
                  style: const TextStyle(color: AppTheme.textSecondary, fontSize: 12),
                ),
              ],
            ),
          ),
          FilledButton(
            onPressed: _buying ? null : _buy,
            child: _buying
                ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                : const Text('Buy'),
          ),
        ],
      ),
    );
  }
}

class _MyActivityTab extends ConsumerWidget {
  const _MyActivityTab();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final profile = ref.watch(myProfileProvider);
    final listings = ref.watch(myShareListingsProvider);
    return AsyncValueView(
      value: profile,
      data: (context, p) => AsyncValueView(
        value: listings,
        onRetry: () => ref.invalidate(myShareListingsProvider),
        data: (context, list) {
          if (list.isEmpty) {
            return const Center(
              child: Padding(
                padding: EdgeInsets.all(24),
                child: Text(
                  "You haven't listed or bought any shares yet.",
                  textAlign: TextAlign.center,
                  style: TextStyle(color: AppTheme.textSecondary),
                ),
              ),
            );
          }
          return RefreshIndicator(
            onRefresh: () async => ref.invalidate(myShareListingsProvider),
            child: ListView.builder(
              padding: const EdgeInsets.all(16),
              itemCount: list.length,
              itemBuilder: (context, index) =>
                  _MyListingTile(listing: list[index], isSeller: list[index].sellerMemberId == p.id),
            ),
          );
        },
      ),
    );
  }
}

class _MyListingTile extends ConsumerStatefulWidget {
  const _MyListingTile({required this.listing, required this.isSeller});
  final ShareListing listing;
  final bool isSeller;

  @override
  ConsumerState<_MyListingTile> createState() => _MyListingTileState();
}

class _MyListingTileState extends ConsumerState<_MyListingTile> {
  bool _cancelling = false;

  Color _statusColor() => switch (widget.listing.status) {
    ShareListingStatus.approved => AppTheme.positive,
    ShareListingStatus.open => const Color(0xFF6C8CFF),
    ShareListingStatus.pendingApproval => const Color(0xFFFFB86C),
    ShareListingStatus.rejected || ShareListingStatus.cancelled => AppTheme.negative,
  };

  Future<void> _cancel() async {
    setState(() => _cancelling = true);
    try {
      await ref.read(shareMarketplaceRepositoryProvider).cancel(widget.listing.id);
      ref.invalidate(myShareListingsProvider);
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text("Couldn't cancel that listing. Please try again.")));
      }
    } finally {
      if (mounted) setState(() => _cancelling = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final color = _statusColor();
    final canCancel = widget.isSeller && widget.listing.status == ShareListingStatus.open;
    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(18)),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                widget.isSeller ? "You're selling" : "You're buying",
                style: const TextStyle(color: AppTheme.textSecondary, fontSize: 12),
              ),
              Chip(
                label: Text(widget.listing.status.label, style: const TextStyle(color: Colors.white, fontSize: 12)),
                backgroundColor: color,
                side: BorderSide.none,
                padding: EdgeInsets.zero,
              ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            Formatters.money(widget.listing.amount),
            style: const TextStyle(color: AppTheme.textPrimary, fontWeight: FontWeight.w700, fontSize: 18),
          ),
          if (widget.listing.status == ShareListingStatus.rejected && widget.listing.rejectionReason != null) ...[
            const SizedBox(height: 4),
            Text(widget.listing.rejectionReason!, style: const TextStyle(color: AppTheme.textSecondary, fontSize: 12)),
          ],
          if (canCancel) ...[
            const SizedBox(height: 12),
            Align(
              alignment: Alignment.centerRight,
              child: OutlinedButton(
                onPressed: _cancelling ? null : _cancel,
                child: _cancelling
                    ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                    : const Text('Cancel listing'),
              ),
            ),
          ],
        ],
      ),
    );
  }
}
