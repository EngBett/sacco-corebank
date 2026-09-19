import 'package:flutter/material.dart';

/// Fade-and-rise entrance used across the wallet screens. Short (320ms, ease-out) and staggered by [delay]; with
/// system reduced motion on it renders in place immediately.
class FadeSlideIn extends StatefulWidget {
  const FadeSlideIn({super.key, required this.child, this.delay = Duration.zero, this.offset = 16});

  final Widget child;
  final Duration delay;
  final double offset;

  /// Standard stagger step between siblings.
  static const step = Duration(milliseconds: 50);

  @override
  State<FadeSlideIn> createState() => _FadeSlideInState();
}

class _FadeSlideInState extends State<FadeSlideIn> with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 320),
  );
  late final Animation<double> _curve = CurvedAnimation(parent: _controller, curve: Curves.easeOutCubic);

  @override
  void initState() {
    super.initState();
    Future<void>.delayed(widget.delay, () {
      if (mounted) _controller.forward();
    });
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (MediaQuery.disableAnimationsOf(context)) return widget.child;
    return AnimatedBuilder(
      animation: _curve,
      child: widget.child,
      builder: (context, child) => Opacity(
        opacity: _curve.value,
        child: Transform.translate(offset: Offset(0, widget.offset * (1 - _curve.value)), child: child),
      ),
    );
  }
}

/// Scales a tappable surface to 0.97 while pressed — press feedback that never shifts layout.
class PressableScale extends StatefulWidget {
  const PressableScale({super.key, required this.child, required this.onTap, this.semanticLabel});

  final Widget child;
  final VoidCallback? onTap;
  final String? semanticLabel;

  @override
  State<PressableScale> createState() => _PressableScaleState();
}

class _PressableScaleState extends State<PressableScale> {
  bool _pressed = false;

  void _set(bool value) {
    if (_pressed != value) setState(() => _pressed = value);
  }

  @override
  Widget build(BuildContext context) {
    return Semantics(
      button: true,
      label: widget.semanticLabel,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTapDown: (_) => _set(true),
        onTapUp: (_) => _set(false),
        onTapCancel: () => _set(false),
        onTap: widget.onTap,
        child: AnimatedScale(
          scale: _pressed ? 0.97 : 1,
          duration: const Duration(milliseconds: 120),
          curve: Curves.easeOut,
          child: widget.child,
        ),
      ),
    );
  }
}

/// Fades a collapsing [SliverAppBar]'s expanded content out as it collapses, so it never shows through behind the
/// pinned toolbar (the expenses reference hides its chart the same way). Must sit inside a [FlexibleSpaceBar].
class FadeOnCollapse extends StatelessWidget {
  const FadeOnCollapse({super.key, required this.child});
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final settings = context.dependOnInheritedWidgetOfExactType<FlexibleSpaceBarSettings>();
    if (settings == null || settings.maxExtent <= settings.minExtent) return child;
    final expanded = ((settings.currentExtent - settings.minExtent) / (settings.maxExtent - settings.minExtent)).clamp(
      0.0,
      1.0,
    );
    final opacity = Curves.easeIn.transform(((expanded - 0.35) / 0.65).clamp(0.0, 1.0));
    return IgnorePointer(
      ignoring: opacity < 0.5,
      child: Opacity(opacity: opacity, child: child),
    );
  }
}
