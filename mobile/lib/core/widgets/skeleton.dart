import 'package:flutter/material.dart';

import '../theme/app_theme.dart';

/// Pulsing placeholder that reserves the space of content still loading, so nothing jumps when it arrives.
class Skeleton extends StatefulWidget {
  const Skeleton({super.key, this.width, required this.height, this.radius = AppTheme.radiusMd});

  final double? width;
  final double height;
  final double radius;

  @override
  State<Skeleton> createState() => _SkeletonState();
}

class _SkeletonState extends State<Skeleton> with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 900),
  )..repeat(reverse: true);

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final reduced = MediaQuery.disableAnimationsOf(context);
    final box = Container(
      width: widget.width,
      height: widget.height,
      decoration: BoxDecoration(color: AppTheme.surfaceRaised, borderRadius: BorderRadius.circular(widget.radius)),
    );
    if (reduced) return box;
    return FadeTransition(opacity: Tween(begin: 0.45, end: 1.0).animate(_controller), child: box);
  }
}
