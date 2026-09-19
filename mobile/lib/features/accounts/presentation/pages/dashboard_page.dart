import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:iconsax_flutter/iconsax_flutter.dart';
import 'package:intl/intl.dart';

import '../../../../core/network/connectivity.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../core/utils/formatters.dart';
import '../../../../core/widgets/motion.dart';
import '../../../../core/widgets/no_connection_view.dart';
import '../../../../core/widgets/skeleton.dart';
import '../../domain/entities/member_profile.dart';
import '../../domain/entities/member_summary.dart';
import '../../domain/entities/savings_account.dart';
import '../providers/accounts_providers.dart';
import '../providers/balance_privacy.dart';
import '../widgets/account_visuals.dart';
import '../widgets/balance_reveal.dart';
import '../widgets/balance_trend_chart.dart';
import '../widgets/transaction_list.dart';

/// Home, in the wallet/expenses reference language: a collapsing header with the total and a balance chart, quick
/// services, the member's accounts as a horizontal card carousel (movies reference), and recent activity. The chart
/// and activity follow whichever account card is centred.
class DashboardPage extends ConsumerStatefulWidget {
  const DashboardPage({super.key});

  @override
  ConsumerState<DashboardPage> createState() => _DashboardPageState();
}

class _DashboardPageState extends ConsumerState<DashboardPage> {
  final _scroll = ScrollController();
  bool _collapsed = false;
  int _selected = 0;

  @override
  void initState() {
    super.initState();
    _scroll.addListener(() {
      final collapsed = _scroll.hasClients && _scroll.offset > 220;
      if (collapsed != _collapsed) setState(() => _collapsed = collapsed);
    });
  }

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _refresh() async {
    ref.invalidate(mySummaryProvider);
    ref.invalidate(myAccountsProvider);
    ref.invalidate(myProfileProvider);
    ref.invalidate(accountHistoryProvider);
    await ref.read(myAccountsProvider.future);
  }

  @override
  Widget build(BuildContext context) {
    final accounts = ref.watch(myAccountsProvider);
    final list = accounts.valueOrNull ?? const <SavingsAccount>[];
    final selected = list.isEmpty ? null : list[_selected.clamp(0, list.length - 1)];

    // The whole home depends on the accounts call; if the network took it down, show the offline view instead.
    if (accounts.hasError && isConnectionError(accounts.error!)) {
      return Scaffold(body: NoConnectionView(compact: true, onRetry: _refresh));
    }

    return Scaffold(
      body: RefreshIndicator(
        onRefresh: _refresh,
        edgeOffset: MediaQuery.paddingOf(context).top + 72,
        child: CustomScrollView(
          controller: _scroll,
          physics: const AlwaysScrollableScrollPhysics(),
          slivers: [
            _WalletHeader(collapsed: _collapsed, selected: selected),
            const SliverToBoxAdapter(child: _QuickServices()),
            SliverToBoxAdapter(
              child: _SectionTitle(
                title: 'Your accounts',
                trailing: list.isEmpty
                    ? null
                    : Text('${list.length} accounts', style: const TextStyle(color: AppTheme.textSecondary)),
              ),
            ),
            SliverToBoxAdapter(
              child: accounts.isLoading && list.isEmpty
                  ? const Padding(
                      padding: EdgeInsets.symmetric(horizontal: 40),
                      child: Skeleton(height: 206, radius: 24),
                    )
                  : _AccountsCarousel(accounts: list, onChanged: (i) => setState(() => _selected = i)),
            ),
            if (selected != null)
              SliverPadding(
                padding: const EdgeInsets.fromLTRB(20, 20, 20, 32),
                sliver: SliverToBoxAdapter(child: _RecentActivity(account: selected)),
              ),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Header
// ---------------------------------------------------------------------------------------------------------------------

class _WalletHeader extends ConsumerWidget {
  const _WalletHeader({required this.collapsed, required this.selected});
  final bool collapsed;
  final SavingsAccount? selected;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final summary = ref.watch(mySummaryProvider).valueOrNull;
    final profile = ref.watch(myProfileProvider);
    final privacy = ref.watch(balancePrivacyProvider);
    final topInset = MediaQuery.paddingOf(context).top;

    return SliverAppBar(
      pinned: true,
      automaticallyImplyLeading: false,
      toolbarHeight: 72,
      expandedHeight: 440,
      titleSpacing: 20,
      backgroundColor: AppTheme.surface,
      surfaceTintColor: Colors.transparent,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(bottom: Radius.circular(AppTheme.radiusXl)),
      ),
      title: AnimatedSwitcher(
        duration: const Duration(milliseconds: 250),
        child: collapsed && summary != null
            ? Column(
                key: const ValueKey('total'),
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Text('Total savings', style: TextStyle(color: AppTheme.textSecondary, fontSize: 12)),
                  BalanceText(summary.visibleTotal, style: AppTheme.money(size: 20)),
                ],
              )
            : _Greeting(key: const ValueKey('greeting'), profile: profile),
      ),
      actions: [
        if (privacy.requirePin)
          _HeaderIconButton(
            icon: privacy.hidden ? Iconsax.eye_copy : Iconsax.eye_slash_copy,
            tooltip: privacy.hidden ? 'Show balances' : 'Hide balances',
            onTap: () => privacy.hidden
                ? ensureBalancesUnlocked(context, ref)
                : ref.read(balancePrivacyProvider.notifier).lock(),
          ),
        _HeaderIconButton(
          icon: Iconsax.receipt_2_copy,
          tooltip: 'Payments and requests',
          onTap: () => context.go('/payments'),
        ),
        const SizedBox(width: 12),
      ],
      flexibleSpace: FlexibleSpaceBar(
        collapseMode: CollapseMode.pin,
        background: FadeOnCollapse(
          child: Padding(
            padding: EdgeInsets.fromLTRB(20, topInset + 72 + 12, 20, 16),
            child: Column(
              children: [
                FadeSlideIn(
                  child: Column(
                    children: [
                      const Text('Total savings', style: TextStyle(color: AppTheme.textSecondary, fontSize: 14)),
                      const SizedBox(height: 8),
                      if (summary == null)
                        const Skeleton(width: 220, height: 44)
                      else
                        _TotalAmount(summary: summary, hidden: privacy.hidden),
                      const SizedBox(height: 6),
                      SizedBox(
                        height: 18,
                        child: summary != null && summary.hasLockedBalances && !privacy.hidden
                            ? const Text(
                                'Excludes balances shown after a paid enquiry',
                                style: TextStyle(color: AppTheme.textSecondary, fontSize: 12),
                              )
                            : null,
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 16),
                Expanded(
                  child: FadeSlideIn(
                    delay: FadeSlideIn.step * 2,
                    child: _SelectedAccountChart(account: selected),
                  ),
                ),
                const SizedBox(height: 12),
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

class _Greeting extends StatelessWidget {
  const _Greeting({super.key, required this.profile});
  final AsyncValue<MemberProfile> profile;

  @override
  Widget build(BuildContext context) {
    final name = profile.valueOrNull?.fullName;
    final first = name?.split(' ').first;
    final accent = Theme.of(context).colorScheme.primary;
    return Row(
      children: [
        CircleAvatar(
          radius: 22,
          backgroundColor: accent.withValues(alpha: 0.16),
          child: Text(
            (first?.isNotEmpty ?? false) ? first![0].toUpperCase() : '·',
            style: TextStyle(color: accent, fontWeight: FontWeight.w800, fontSize: 17),
          ),
        ),
        const SizedBox(width: 12),
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('Welcome back', style: TextStyle(color: AppTheme.textSecondary, fontSize: 12)),
            Text(
              first ?? '',
              style: const TextStyle(color: AppTheme.textPrimary, fontSize: 18, fontWeight: FontWeight.w700),
            ),
          ],
        ),
      ],
    );
  }
}

class _TotalAmount extends StatelessWidget {
  const _TotalAmount({required this.summary, required this.hidden});
  final MemberSavingsSummary summary;
  final bool hidden;

  @override
  Widget build(BuildContext context) {
    final digits = hidden ? '••••••' : Formatters.money(summary.visibleTotal).replaceFirst('KES', '').trim();
    return Semantics(
      label: hidden ? 'Total savings hidden' : 'Total savings ${Formatters.money(summary.visibleTotal)}',
      excludeSemantics: true,
      child: FittedBox(
        fit: BoxFit.scaleDown,
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.baseline,
          textBaseline: TextBaseline.alphabetic,
          children: [
            Text(
              'KES ',
              style: AppTheme.money(size: 20, weight: FontWeight.w600, color: AppTheme.textSecondary),
            ),
            Text(digits, style: AppTheme.money(size: 38, weight: FontWeight.w800)),
          ],
        ),
      ),
    );
  }
}

class _HeaderIconButton extends StatelessWidget {
  const _HeaderIconButton({required this.icon, required this.tooltip, required this.onTap});
  final IconData icon;
  final String tooltip;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(left: 8),
      child: IconButton(
        tooltip: tooltip,
        onPressed: onTap,
        style: IconButton.styleFrom(
          backgroundColor: AppTheme.surfaceRaised,
          fixedSize: const Size(44, 44),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(AppTheme.radiusSm)),
        ),
        icon: Icon(icon, size: 20, color: AppTheme.textPrimary),
      ),
    );
  }
}

/// The chart area for the centred account, or why it can't be drawn yet (hidden, fee-locked, loading, offline).
class _SelectedAccountChart extends ConsumerWidget {
  const _SelectedAccountChart({required this.account});
  final SavingsAccount? account;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final account = this.account;
    if (account == null) return const Skeleton(height: double.infinity);
    final privacy = ref.watch(balancePrivacyProvider);

    Widget labelled(Widget chart, {String? change}) => Column(
      children: [
        Row(
          children: [
            Icon(AccountVisuals.icon(account.kind), size: 16, color: AppTheme.textSecondary),
            const SizedBox(width: 6),
            Expanded(
              child: Text(
                '${account.kind.label} · last $historyMonths months',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(color: AppTheme.textSecondary, fontSize: 13, fontWeight: FontWeight.w600),
              ),
            ),
            if (change != null) _ChangeBadge(text: change),
          ],
        ),
        const SizedBox(height: 10),
        Expanded(child: chart),
      ],
    );

    if (privacy.hidden) {
      return labelled(
        _ChartNotice(
          icon: Iconsax.eye_slash_copy,
          message: 'Balances are hidden on this phone',
          action: unlockActionLabel(privacy),
          onAction: () => ensureBalancesUnlocked(context, ref),
        ),
      );
    }
    if (account.balanceLocked) return labelled(_LockedNotice(account: account));

    final history = ref.watch(accountHistoryProvider(account.accountNumber));
    return history.when(
      loading: () => labelled(const Skeleton(height: double.infinity)),
      error: (error, _) => labelled(
        _isBalanceLocked(error)
            ? _LockedNotice(account: account)
            : _ChartNotice(
                icon: isConnectionError(error) ? Iconsax.cloud_cross_copy : Iconsax.danger_copy,
                message: isConnectionError(error) ? "Can't reach your SACCO" : "Couldn't load this chart",
                action: 'Retry',
                onAction: () => ref.invalidate(accountHistoryProvider(account.accountNumber)),
              ),
      ),
      data: (statement) {
        final start = statement.openingBalance;
        final change = start == 0 ? null : (statement.closingBalance - start) / start.abs() * 100;
        return labelled(
          BalanceTrendChart(key: ValueKey(account.accountNumber), statement: statement, label: account.kind.label),
          change: change == null || change.abs() < 0.05
              ? null
              : '${change >= 0 ? '+' : '−'}${change.abs().toStringAsFixed(1)}%',
        );
      },
    );
  }
}

bool _isBalanceLocked(Object error) =>
    error is DioException &&
    error.response?.data is Map &&
    (error.response!.data as Map)['title'] == 'savings.balance.locked';

class _ChangeBadge extends StatelessWidget {
  const _ChangeBadge({required this.text});
  final String text;

  @override
  Widget build(BuildContext context) {
    final up = !text.startsWith('−');
    final color = up ? AppTheme.positive : AppTheme.negative;
    return Semantics(
      label: '${up ? 'Up' : 'Down'} ${text.substring(1)} over the period',
      excludeSemantics: true,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
        decoration: BoxDecoration(color: color.withValues(alpha: 0.14), borderRadius: BorderRadius.circular(20)),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(up ? Iconsax.trend_up_copy : Iconsax.arrow_down_copy, size: 14, color: color),
            const SizedBox(width: 4),
            Text(text, style: AppTheme.money(size: 12, color: color)),
          ],
        ),
      ),
    );
  }
}

class _ChartNotice extends StatelessWidget {
  const _ChartNotice({required this.icon, required this.message, required this.action, required this.onAction});
  final IconData icon;
  final String message;
  final String action;
  final VoidCallback onAction;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(AppTheme.radiusLg),
        border: Border.all(color: AppTheme.border),
      ),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, color: AppTheme.textSecondary, size: 28),
          const SizedBox(height: 10),
          Text(
            message,
            textAlign: TextAlign.center,
            style: const TextStyle(color: AppTheme.textSecondary),
          ),
          const SizedBox(height: 6),
          TextButton(onPressed: onAction, child: Text(action)),
        ],
      ),
    );
  }
}

class _LockedNotice extends ConsumerWidget {
  const _LockedNotice({required this.account});
  final SavingsAccount account;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final fee = account.balanceEnquiryFee ?? 0;
    return _ChartNotice(
      icon: Iconsax.lock_1_copy,
      message: 'This balance is shown after a balance enquiry',
      action: fee > 0 ? 'View balance · ${Formatters.money(fee)}' : 'View balance',
      onAction: () => revealFeeLockedBalance(context, ref, account),
    );
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Quick services
// ---------------------------------------------------------------------------------------------------------------------

class _QuickServices extends StatelessWidget {
  const _QuickServices();

  @override
  Widget build(BuildContext context) {
    final services = <(IconData, String, VoidCallback)>[
      (Iconsax.import_1_copy, 'Deposit', () => context.push('/deposit')),
      (Iconsax.export_1_copy, 'Withdraw', () => context.push('/withdraw')),
      (Iconsax.chart_2_copy, 'Shares', () => context.push('/shares-marketplace')),
      (Iconsax.money_recive_copy, 'Dividends', () => context.go('/dividends')),
    ];
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 28, 20, 8),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          for (final (i, service) in services.indexed)
            FadeSlideIn(
              delay: FadeSlideIn.step * (i + 3),
              child: PressableScale(
                semanticLabel: service.$2,
                onTap: service.$3,
                child: SizedBox(
                  width: 72,
                  child: Column(
                    children: [
                      Container(
                        width: 60,
                        height: 60,
                        decoration: BoxDecoration(
                          color: AppTheme.surface,
                          borderRadius: BorderRadius.circular(AppTheme.radiusSm),
                          border: Border.all(color: AppTheme.border),
                        ),
                        child: Icon(service.$1, color: AppTheme.textPrimary, size: 24),
                      ),
                      const SizedBox(height: 10),
                      Text(
                        service.$2,
                        style: const TextStyle(
                          color: AppTheme.textSecondary,
                          fontSize: 13,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}

class _SectionTitle extends StatelessWidget {
  const _SectionTitle({required this.title, this.trailing});
  final String title;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 24, 20, 14),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Semantics(header: true, child: Text(title, style: Theme.of(context).textTheme.titleLarge)),
          ?trailing,
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Accounts carousel
// ---------------------------------------------------------------------------------------------------------------------

/// Accounts as swipeable cards with the centred one enlarged — the movies reference's carousel.
class _AccountsCarousel extends StatefulWidget {
  const _AccountsCarousel({required this.accounts, required this.onChanged});
  final List<SavingsAccount> accounts;
  final ValueChanged<int> onChanged;

  @override
  State<_AccountsCarousel> createState() => _AccountsCarouselState();
}

class _AccountsCarouselState extends State<_AccountsCarousel> {
  final _controller = PageController(viewportFraction: 0.8);
  int _page = 0;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final accounts = widget.accounts;
    if (accounts.isEmpty) {
      return const Padding(
        padding: EdgeInsets.symmetric(horizontal: 20),
        child: Text('No accounts yet. Visit a branch to open one.', style: TextStyle(color: AppTheme.textSecondary)),
      );
    }
    return Column(
      children: [
        SizedBox(
          height: 206,
          child: PageView.builder(
            controller: _controller,
            itemCount: accounts.length,
            onPageChanged: (i) {
              setState(() => _page = i);
              widget.onChanged(i);
            },
            itemBuilder: (context, index) => AnimatedBuilder(
              animation: _controller,
              builder: (context, child) {
                final page = _controller.hasClients && _controller.position.haveDimensions
                    ? _controller.page ?? _page.toDouble()
                    : _page.toDouble();
                final distance = (page - index).abs().clamp(0.0, 1.0);
                return Transform.scale(
                  scale: 1 - distance * 0.08,
                  child: Opacity(opacity: 1 - distance * 0.35, child: child),
                );
              },
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 6),
                child: _AccountCard(account: accounts[index], current: index == _page),
              ),
            ),
          ),
        ),
        const SizedBox(height: 14),
        Semantics(
          label: 'Account ${_page + 1} of ${accounts.length}',
          excludeSemantics: true,
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              for (var i = 0; i < accounts.length; i++)
                AnimatedContainer(
                  duration: const Duration(milliseconds: 250),
                  curve: Curves.easeOutCubic,
                  margin: const EdgeInsets.symmetric(horizontal: 3),
                  width: i == _page ? 22 : 6,
                  height: 6,
                  decoration: BoxDecoration(
                    color: i == _page ? Theme.of(context).colorScheme.primary : AppTheme.border,
                    borderRadius: BorderRadius.circular(6),
                  ),
                ),
            ],
          ),
        ),
      ],
    );
  }
}

class _AccountCard extends ConsumerWidget {
  const _AccountCard({required this.account, required this.current});
  final SavingsAccount account;
  final bool current;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final hidden = ref.watch(balancePrivacyProvider).hidden;
    final colors = AccountVisuals.gradient(account.kind);
    final locked = account.balanceLocked;
    final balanceLabel = hidden || locked || account.balance == null
        ? 'balance hidden'
        : Formatters.money(account.balance!);

    return PressableScale(
      semanticLabel: '${account.kind.label}, ${account.accountNumber}, $balanceLabel. Opens statement.',
      onTap: () => openStatement(context, ref, account),
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 300),
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(24),
          gradient: LinearGradient(begin: Alignment.topLeft, end: Alignment.bottomRight, colors: colors),
          boxShadow: [
            if (current)
              BoxShadow(color: colors.first.withValues(alpha: 0.35), blurRadius: 24, offset: const Offset(0, 12)),
          ],
        ),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(24),
          child: Stack(
            children: [
              const Positioned(right: -40, top: -50, child: _Orb(size: 170)),
              const Positioned(right: 40, bottom: -70, child: _Orb(size: 130)),
              Padding(
                padding: const EdgeInsets.all(20),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Container(
                          width: 40,
                          height: 40,
                          decoration: BoxDecoration(
                            color: Colors.white.withValues(alpha: 0.16),
                            borderRadius: BorderRadius.circular(12),
                          ),
                          child: Icon(AccountVisuals.icon(account.kind), color: Colors.white, size: 20),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                account.kind.label,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w700, fontSize: 16),
                              ),
                              Text(
                                AccountVisuals.blurb(account.kind),
                                style: TextStyle(color: Colors.white.withValues(alpha: 0.78), fontSize: 12),
                              ),
                            ],
                          ),
                        ),
                        Icon(Iconsax.arrow_right_3_copy, color: Colors.white.withValues(alpha: 0.85), size: 18),
                      ],
                    ),
                    const Spacer(),
                    Text(
                      account.accountNumber,
                      style: TextStyle(
                        color: Colors.white.withValues(alpha: 0.78),
                        fontSize: 13,
                        letterSpacing: 1.2,
                        fontFeatures: AppTheme.moneyFigures,
                      ),
                    ),
                    const SizedBox(height: 4),
                    if (locked && !hidden)
                      _CardLockedBalance(fee: account.balanceEnquiryFee)
                    else
                      FittedBox(
                        fit: BoxFit.scaleDown,
                        alignment: Alignment.centerLeft,
                        child: BalanceText(
                          account.balance,
                          style: AppTheme.money(size: 28, weight: FontWeight.w800, color: Colors.white),
                        ),
                      ),
                    const SizedBox(height: 8),
                    AnimatedOpacity(
                      duration: const Duration(milliseconds: 250),
                      opacity: current ? 1 : 0,
                      child: _CardMeta(account: account, hidden: hidden),
                    ),
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

class _Orb extends StatelessWidget {
  const _Orb({required this.size});
  final double size;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(shape: BoxShape.circle, color: Colors.white.withValues(alpha: 0.07)),
    );
  }
}

class _CardLockedBalance extends StatelessWidget {
  const _CardLockedBalance({required this.fee});
  final double? fee;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Text(
          'KES ••••••',
          style: AppTheme.money(size: 28, weight: FontWeight.w800, color: Colors.white),
        ),
        const SizedBox(width: 10),
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
          decoration: BoxDecoration(
            color: Colors.white.withValues(alpha: 0.18),
            borderRadius: BorderRadius.circular(20),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Iconsax.lock_1_copy, color: Colors.white, size: 12),
              const SizedBox(width: 4),
              Text(
                fee != null && fee! > 0 ? Formatters.money(fee!) : 'View',
                style: AppTheme.money(size: 11, weight: FontWeight.w700, color: Colors.white),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _CardMeta extends StatelessWidget {
  const _CardMeta({required this.account, required this.hidden});
  final SavingsAccount account;
  final bool hidden;

  @override
  Widget build(BuildContext context) {
    final style = TextStyle(
      color: Colors.white.withValues(alpha: 0.82),
      fontSize: 12,
      fontWeight: FontWeight.w600,
      fontFeatures: AppTheme.moneyFigures,
    );
    final held = account.heldAmount ?? 0;
    if (account.balanceLocked) return Text('Tap to view your balance', style: style);
    return Row(
      children: [
        Icon(Iconsax.wallet_check_copy, size: 14, color: Colors.white.withValues(alpha: 0.82)),
        const SizedBox(width: 4),
        Flexible(
          child: Text(
            hidden || account.availableBalance == null
                ? 'Available ••••'
                : 'Available ${Formatters.money(account.availableBalance!)}',
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: style,
          ),
        ),
        if (held > 0 && !hidden) ...[
          const SizedBox(width: 10),
          Icon(Iconsax.lock_1_copy, size: 14, color: Colors.white.withValues(alpha: 0.82)),
          const SizedBox(width: 4),
          Text(
            '${NumberFormat.compactCurrency(locale: 'en_KE', symbol: 'KES ').format(held)} held',
            maxLines: 1,
            style: style,
          ),
        ],
      ],
    );
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Recent activity
// ---------------------------------------------------------------------------------------------------------------------

class _RecentActivity extends ConsumerWidget {
  const _RecentActivity({required this.account});
  final SavingsAccount account;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final hidden = ref.watch(balancePrivacyProvider).hidden;

    Widget section(Widget body) => Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Semantics(header: true, child: Text('Recent activity', style: Theme.of(context).textTheme.titleLarge)),
            TextButton(onPressed: () => openStatement(context, ref, account), child: const Text('See all')),
          ],
        ),
        const SizedBox(height: 4),
        body,
      ],
    );

    if (hidden || account.balanceLocked) {
      return section(
        _InlineNotice(
          icon: hidden ? Iconsax.eye_slash_copy : Iconsax.lock_1_copy,
          message: hidden
              ? 'Unlock your balances to see recent activity.'
              : 'Activity on this account is shown after a balance enquiry.',
          action: hidden ? unlockActionLabel(ref.watch(balancePrivacyProvider)) : 'View',
          onAction: () => hidden ? ensureBalancesUnlocked(context, ref) : revealFeeLockedBalance(context, ref, account),
        ),
      );
    }

    final history = ref.watch(accountHistoryProvider(account.accountNumber));
    return section(
      history.when(
        loading: () => Column(
          children: List.generate(
            3,
            (_) => const Padding(padding: EdgeInsets.only(bottom: 10), child: Skeleton(height: 68)),
          ),
        ),
        error: (error, _) => _InlineNotice(
          icon: isConnectionError(error) ? Iconsax.cloud_cross_copy : Iconsax.danger_copy,
          message: isConnectionError(error) ? "Can't reach your SACCO right now." : "Couldn't load recent activity.",
          action: 'Retry',
          onAction: () => ref.invalidate(accountHistoryProvider(account.accountNumber)),
        ),
        data: (statement) => statement.lines.isEmpty
            ? const _InlineNotice(icon: Iconsax.receipt_item_copy, message: 'No transactions in the last six months.')
            : Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  for (final (i, w) in buildTransactionGroups(context, statement.lines, limit: 6).indexed)
                    FadeSlideIn(delay: FadeSlideIn.step * i, child: w),
                ],
              ),
      ),
    );
  }
}

class _InlineNotice extends StatelessWidget {
  const _InlineNotice({required this.icon, required this.message, this.action, this.onAction});
  final IconData icon;
  final String message;
  final String? action;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(AppTheme.radiusMd)),
      child: Row(
        children: [
          Icon(icon, color: AppTheme.textSecondary),
          const SizedBox(width: 12),
          Expanded(
            child: Text(message, style: const TextStyle(color: AppTheme.textSecondary)),
          ),
          if (action != null) TextButton(onPressed: onAction, child: Text(action!)),
        ],
      ),
    );
  }
}
