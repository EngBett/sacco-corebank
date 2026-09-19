import 'dart:async';

import 'package:flutter/material.dart';

import '../../../../core/theme/app_theme.dart';

/// One scene of the auth hero: a large icon in a ringed disc with two small badges beside it.
class AuthScene {
  const AuthScene({required this.icon, required this.badges});

  final IconData icon;

  /// Exactly two small icons, drawn top-right and bottom-left of the disc.
  final List<IconData> badges;
}

/// The cross-fading hero from the login and verification examples, drawn in code rather than loaded from a remote
/// illustration library (no network dependency on the sign-in screen, no third-party artwork licence). Cycles every
/// [interval]; with system reduced motion on it shows the first scene only. Purely decorative, so hidden from
/// screen readers.
class AuthIllustration extends StatefulWidget {
  const AuthIllustration({
    super.key,
    required this.scenes,
    this.height = 220,
    this.interval = const Duration(seconds: 5),
  });

  final List<AuthScene> scenes;
  final double height;
  final Duration interval;

  @override
  State<AuthIllustration> createState() => _AuthIllustrationState();
}

class _AuthIllustrationState extends State<AuthIllustration> {
  int _index = 0;
  Timer? _timer;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _timer?.cancel();
    if (!MediaQuery.disableAnimationsOf(context) && widget.scenes.length > 1) {
      _timer = Timer.periodic(widget.interval, (_) {
        if (mounted) setState(() => _index = (_index + 1) % widget.scenes.length);
      });
    }
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final accent = Theme.of(context).colorScheme.primary;
    return ExcludeSemantics(
      child: SizedBox(
        height: widget.height,
        child: Stack(
          fit: StackFit.expand,
          children: [
            for (var i = 0; i < widget.scenes.length; i++)
              AnimatedOpacity(
                opacity: i == _index ? 1 : 0,
                duration: const Duration(milliseconds: 800),
                curve: Curves.easeInOut,
                child: _Scene(scene: widget.scenes[i], accent: accent, size: widget.height),
              ),
          ],
        ),
      ),
    );
  }
}

class _Scene extends StatelessWidget {
  const _Scene({required this.scene, required this.accent, required this.size});

  final AuthScene scene;
  final Color accent;
  final double size;

  @override
  Widget build(BuildContext context) {
    final disc = size * 0.5;
    return Center(
      child: SizedBox.square(
        dimension: size,
        child: Stack(
          alignment: Alignment.center,
          children: [
            _Ring(diameter: size * 0.92, color: accent.withValues(alpha: 0.08)),
            _Ring(diameter: size * 0.72, color: accent.withValues(alpha: 0.16)),
            Container(
              width: disc,
              height: disc,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                gradient: LinearGradient(
                  begin: Alignment.topLeft,
                  end: Alignment.bottomRight,
                  colors: [AppTheme.surfaceRaised, AppTheme.surface],
                ),
                border: Border.all(color: AppTheme.border),
              ),
              child: Icon(scene.icon, size: disc * 0.46, color: accent),
            ),
            Positioned(
              top: size * 0.12,
              right: size * 0.1,
              child: _Badge(icon: scene.badges[0]),
            ),
            Positioned(
              bottom: size * 0.14,
              left: size * 0.08,
              child: _Badge(icon: scene.badges[1]),
            ),
          ],
        ),
      ),
    );
  }
}

class _Ring extends StatelessWidget {
  const _Ring({required this.diameter, required this.color});

  final double diameter;
  final Color color;

  @override
  Widget build(BuildContext context) => Container(
    width: diameter,
    height: diameter,
    decoration: BoxDecoration(
      shape: BoxShape.circle,
      border: Border.all(color: color, width: 1.5),
    ),
  );
}

class _Badge extends StatelessWidget {
  const _Badge({required this.icon});

  final IconData icon;

  @override
  Widget build(BuildContext context) => Container(
    width: 44,
    height: 44,
    decoration: BoxDecoration(
      color: AppTheme.surfaceRaised,
      borderRadius: BorderRadius.circular(AppTheme.radiusSm),
      border: Border.all(color: AppTheme.border),
    ),
    child: Icon(icon, size: 20, color: AppTheme.textPrimary),
  );
}
