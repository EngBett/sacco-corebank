import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/di/service_locator.dart';
import 'core/router/app_router.dart';
import 'core/theme/app_theme.dart';
import 'core/widgets/no_connection_view.dart';
import 'features/branding/presentation/providers/branding_providers.dart';

void main() {
  setupServiceLocator();
  runApp(const ProviderScope(child: SaccoMemberApp()));
}

class SaccoMemberApp extends ConsumerWidget {
  const SaccoMemberApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = ref.watch(routerProvider);
    final branding = ref.watch(tenantBrandingProvider);

    return MaterialApp.router(
      title: 'SACCO Member',
      debugShowCheckedModeBanner: false,
      theme: branding.maybeWhen(
        data: (b) => AppTheme.dark(primaryHex: b.primaryColor),
        orElse: AppTheme.dark,
      ),
      routerConfig: router,
      builder: (context, child) => ConnectivityGate(child: child ?? const SizedBox.shrink()),
    );
  }
}
