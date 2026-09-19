import 'package:flutter/material.dart';

import '../../../../core/theme/app_theme.dart';

/// Outlined field from the login example: no fill, a quiet 1.5px border that takes the accent on focus, a leading
/// icon and a floating label.
InputDecoration authFieldDecoration(
  BuildContext context, {
  required String label,
  required IconData icon,
  String? hint,
  Widget? suffix,
}) {
  final radius = BorderRadius.circular(AppTheme.radiusSm);
  final accent = Theme.of(context).colorScheme.primary;
  return InputDecoration(
    filled: false,
    labelText: label,
    hintText: hint,
    counterText: '',
    prefixIcon: Icon(icon, size: 20, color: AppTheme.textSecondary),
    suffixIcon: suffix,
    floatingLabelStyle: TextStyle(color: accent, fontSize: 16, fontWeight: FontWeight.w600),
    enabledBorder: OutlineInputBorder(
      borderRadius: radius,
      borderSide: const BorderSide(color: AppTheme.border, width: 1.5),
    ),
    focusedBorder: OutlineInputBorder(
      borderRadius: radius,
      borderSide: BorderSide(color: accent, width: 1.5),
    ),
    errorBorder: OutlineInputBorder(
      borderRadius: radius,
      borderSide: const BorderSide(color: AppTheme.negative, width: 1.5),
    ),
    focusedErrorBorder: OutlineInputBorder(
      borderRadius: radius,
      borderSide: const BorderSide(color: AppTheme.negative, width: 1.5),
    ),
  );
}

/// Short explanatory bottom sheet with a title, body and a single "Got it" dismiss.
Future<void> showAuthInfoSheet(
  BuildContext context, {
  required IconData icon,
  required String title,
  required String body,
}) {
  return showModalBottomSheet<void>(
    context: context,
    builder: (context) => SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(24, 0, 24, 24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Icon(icon, size: 36, color: Theme.of(context).colorScheme.primary),
            const SizedBox(height: 16),
            Text(title, style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 8),
            Text(body, style: const TextStyle(color: AppTheme.textSecondary, height: 1.5)),
            const SizedBox(height: 24),
            FilledButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Got it')),
          ],
        ),
      ),
    ),
  );
}
