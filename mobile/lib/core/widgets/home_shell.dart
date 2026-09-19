import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:iconsax_flutter/iconsax_flutter.dart';

import '../../features/accounts/presentation/providers/balance_privacy.dart';
import 'pill_nav_bar.dart';

class HomeShell extends ConsumerStatefulWidget {
  const HomeShell({super.key, required this.child});
  final Widget child;

  @override
  ConsumerState<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends ConsumerState<HomeShell> with WidgetsBindingObserver {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  /// Leaving the app re-hides balances when "Require PIN to show balances" is on.
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.paused || state == AppLifecycleState.hidden) {
      ref.read(balancePrivacyProvider.notifier).lock();
    }
  }

  static const _tabs = ['/dashboard', '/payments', '/dividends', '/profile'];

  int _indexFor(String location) {
    final index = _tabs.indexWhere((t) => location.startsWith(t));
    return index == -1 ? 0 : index;
  }

  @override
  Widget build(BuildContext context) {
    final location = GoRouterState.of(context).matchedLocation;
    return Scaffold(
      body: widget.child,
      bottomNavigationBar: PillNavBar(
        selectedIndex: _indexFor(location),
        onSelected: (index) => context.go(_tabs[index]),
        items: const [
          PillNavItem(icon: Iconsax.home_2_copy, label: 'Home'),
          PillNavItem(icon: Iconsax.arrow_swap_horizontal_copy, label: 'Payments'),
          PillNavItem(icon: Iconsax.money_recive_copy, label: 'Dividends'),
          PillNavItem(icon: Iconsax.profile_circle_copy, label: 'Profile'),
        ],
      ),
    );
  }
}
