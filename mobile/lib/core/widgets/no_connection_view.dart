import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:iconsax_flutter/iconsax_flutter.dart';

import '../network/connectivity.dart';
import '../theme/app_theme.dart';
import 'motion.dart';

/// Offline state, after the no-connection reference: an illustration above a rounded sheet with one clear way out.
/// [compact] is the in-page variant shown where a section failed to load.
class NoConnectionView extends StatefulWidget {
  const NoConnectionView({super.key, required this.onRetry, this.compact = false});

  final Future<void> Function() onRetry;
  final bool compact;

  @override
  State<NoConnectionView> createState() => _NoConnectionViewState();
}

class _NoConnectionViewState extends State<NoConnectionView> {
  bool _retrying = false;

  Future<void> _retry() async {
    setState(() => _retrying = true);
    try {
      await widget.onRetry();
    } finally {
      if (mounted) setState(() => _retrying = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final button = SizedBox(
      width: double.infinity,
      child: FilledButton.icon(
        onPressed: _retrying ? null : _retry,
        icon: _retrying
            ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
            : const Icon(Iconsax.refresh_copy, size: 20),
        label: Text(_retrying ? 'Checking…' : 'Try again'),
      ),
    );

    if (widget.compact) {
      return Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const SizedBox(height: 140, child: _OfflineIllustration(size: 120)),
              const SizedBox(height: 16),
              Text("Can't reach your SACCO", style: Theme.of(context).textTheme.titleMedium),
              const SizedBox(height: 6),
              const Text(
                'Check your mobile data or Wi-Fi, then try again.',
                textAlign: TextAlign.center,
                style: TextStyle(color: AppTheme.textSecondary),
              ),
              const SizedBox(height: 20),
              SizedBox(width: 220, child: button),
            ],
          ),
        ),
      );
    }

    return Scaffold(
      body: Column(
        children: [
          const Expanded(
            child: SafeArea(child: Center(child: _OfflineIllustration(size: 220))),
          ),
          FadeSlideIn(
            offset: 40,
            child: Container(
              width: double.infinity,
              padding: EdgeInsets.fromLTRB(32, 36, 32, 32 + MediaQuery.paddingOf(context).bottom),
              decoration: const BoxDecoration(
                color: AppTheme.surface,
                borderRadius: BorderRadius.vertical(top: Radius.circular(48)),
              ),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Semantics(
                    header: true,
                    child: Text("You're offline", style: Theme.of(context).textTheme.headlineSmall),
                  ),
                  const SizedBox(height: 12),
                  const Text(
                    "We can't reach your SACCO right now. Check your mobile data or Wi-Fi, then try again.",
                    textAlign: TextAlign.center,
                    style: TextStyle(color: AppTheme.textSecondary, fontSize: 16, height: 1.5),
                  ),
                  const SizedBox(height: 32),
                  button,
                  const SizedBox(height: 12),
                  const Text(
                    'Nothing you started is lost — pending requests are safe on the server.',
                    textAlign: TextAlign.center,
                    style: TextStyle(color: AppTheme.textSecondary, fontSize: 12),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Code-drawn illustration: a cloud with a cross inside softly pulsing signal rings. Static under reduced motion.
class _OfflineIllustration extends StatefulWidget {
  const _OfflineIllustration({required this.size});
  final double size;

  @override
  State<_OfflineIllustration> createState() => _OfflineIllustrationState();
}

class _OfflineIllustrationState extends State<_OfflineIllustration> with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(vsync: this, duration: const Duration(seconds: 3))
    ..repeat();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final accent = Theme.of(context).colorScheme.primary;
    final reduced = MediaQuery.disableAnimationsOf(context);
    final size = widget.size;

    Widget ring(double t) {
      final scale = 0.55 + 0.45 * t;
      return Opacity(
        opacity: (1 - t) * 0.5,
        child: Container(
          width: size * scale,
          height: size * scale,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            border: Border.all(color: accent, width: 1.5),
          ),
        ),
      );
    }

    return Semantics(
      label: 'No internet connection',
      image: true,
      child: SizedBox(
        width: size,
        height: size,
        child: AnimatedBuilder(
          animation: _controller,
          builder: (context, _) {
            final t = reduced ? 0.4 : _controller.value;
            return Stack(
              alignment: Alignment.center,
              children: [
                ring(t),
                ring((t + 0.5) % 1),
                Container(
                  width: size * 0.5,
                  height: size * 0.5,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    gradient: LinearGradient(
                      begin: Alignment.topLeft,
                      end: Alignment.bottomRight,
                      colors: [AppTheme.surfaceRaised, AppTheme.surface],
                    ),
                    border: Border.all(color: AppTheme.border),
                  ),
                  child: Icon(Iconsax.cloud_cross_copy, size: size * 0.22, color: accent),
                ),
              ],
            );
          },
        ),
      ),
    );
  }
}

/// Covers the whole app with the offline screen whenever the device loses its network, and gets out of the way the
/// moment it's back. Retrying re-checks connectivity.
class ConnectivityGate extends ConsumerWidget {
  const ConnectivityGate({super.key, required this.child});
  final Widget child;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final offline = ref.watch(isOnlineProvider).valueOrNull == false;
    return Stack(
      children: [
        child,
        AnimatedSwitcher(
          duration: const Duration(milliseconds: 250),
          child: offline
              ? NoConnectionView(
                  key: const ValueKey('offline'),
                  onRetry: () async {
                    ref.invalidate(isOnlineProvider);
                    await ref.read(isOnlineProvider.future);
                  },
                )
              : const SizedBox.shrink(key: ValueKey('online')),
        ),
      ],
    );
  }
}
