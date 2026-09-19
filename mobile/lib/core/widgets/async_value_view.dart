import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:iconsax_flutter/iconsax_flutter.dart';

import '../network/connectivity.dart';
import '../theme/app_theme.dart';
import 'no_connection_view.dart';

/// Renders an [AsyncValue] with a consistent loading/error/data shape so every screen doesn't hand-roll its own
/// `when(...)`. A network failure gets the offline view; anything else a short message with a retry.
class AsyncValueView<T> extends StatelessWidget {
  const AsyncValueView({super.key, required this.value, required this.data, this.onRetry, this.loading});

  final AsyncValue<T> value;
  final Widget Function(BuildContext context, T data) data;
  final VoidCallback? onRetry;

  /// Placeholder shaped like the content (skeleton); a spinner when omitted.
  final Widget? loading;

  @override
  Widget build(BuildContext context) {
    return value.when(
      data: (d) => data(context, d),
      loading: () =>
          loading ??
          const Center(
            child: Padding(padding: EdgeInsets.all(32), child: CircularProgressIndicator()),
          ),
      error: (error, _) {
        if (isConnectionError(error)) {
          return NoConnectionView(compact: true, onRetry: () async => onRetry?.call());
        }
        return Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Iconsax.danger_copy, size: 40, color: AppTheme.negative),
                const SizedBox(height: 12),
                const Text(
                  'Something went wrong loading this. Please try again.',
                  textAlign: TextAlign.center,
                  style: TextStyle(color: AppTheme.textSecondary),
                ),
                if (onRetry != null) ...[
                  const SizedBox(height: 12),
                  OutlinedButton(onPressed: onRetry, child: const Text('Retry')),
                ],
              ],
            ),
          ),
        );
      },
    );
  }
}
