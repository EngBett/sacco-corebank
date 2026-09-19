import 'package:flutter/material.dart';

/// Shown only while [AuthController] is restoring a session from secure
/// storage (a brief check on cold start) — the router then redirects away.
class SplashPage extends StatelessWidget {
  const SplashPage({super.key});

  @override
  Widget build(BuildContext context) {
    return const Scaffold(body: Center(child: CircularProgressIndicator()));
  }
}
