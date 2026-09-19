import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../providers/auth_controller.dart';

/// Shown on a cold start when a stored session exists and this device supports biometrics — the
/// "quick unlock" step Equity/KCB/M-Pesa apps use instead of asking for the PIN again (ADR 0008).
class BiometricGatePage extends ConsumerStatefulWidget {
  const BiometricGatePage({super.key});

  @override
  ConsumerState<BiometricGatePage> createState() => _BiometricGatePageState();
}

class _BiometricGatePageState extends ConsumerState<BiometricGatePage> {
  bool _prompting = false;
  bool _failed = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _prompt());
  }

  Future<void> _prompt() async {
    setState(() {
      _prompting = true;
      _failed = false;
    });
    final ok = await ref.read(authControllerProvider.notifier).unlockWithBiometrics();
    if (!mounted) return;
    setState(() {
      _prompting = false;
      _failed = !ok;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: Padding(
            padding: const EdgeInsets.all(32),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(Icons.fingerprint, size: 72, color: Theme.of(context).colorScheme.primary),
                const SizedBox(height: 24),
                Text(
                  _prompting ? 'Verifying…' : (_failed ? "That didn't work" : 'Unlock to continue'),
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                const SizedBox(height: 32),
                if (!_prompting) ...[
                  FilledButton(onPressed: _prompt, child: const Text('Try again')),
                  const SizedBox(height: 12),
                  TextButton(
                    onPressed: () => ref.read(authControllerProvider.notifier).useCredentialsInstead(),
                    child: const Text('Use phone number and PIN instead'),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
