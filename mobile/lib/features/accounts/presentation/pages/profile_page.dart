import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../../core/theme/app_theme.dart';
import '../../../../core/utils/formatters.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../../auth/presentation/providers/auth_controller.dart';
import '../providers/accounts_providers.dart';
import '../providers/balance_privacy.dart';
import '../widgets/balance_reveal.dart';

class ProfilePage extends ConsumerWidget {
  const ProfilePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final profile = ref.watch(myProfileProvider);
    final accounts = ref.watch(myAccountsProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Profile')),
      body: AsyncValueView(
        value: profile,
        onRetry: () => ref.invalidate(myProfileProvider),
        data: (context, p) => ListView(
          padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
          children: [
            Container(
              padding: const EdgeInsets.all(16),
              decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(20)),
              child: Row(
                children: [
                  CircleAvatar(
                    radius: 28,
                    backgroundColor: Theme.of(context).colorScheme.primary.withValues(alpha: 0.18),
                    child: Text(
                      p.fullName.isEmpty ? '?' : p.fullName[0].toUpperCase(),
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.primary,
                        fontWeight: FontWeight.w700,
                        fontSize: 22,
                      ),
                    ),
                  ),
                  const SizedBox(width: 16),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          p.fullName,
                          style: const TextStyle(
                            color: AppTheme.textPrimary,
                            fontWeight: FontWeight.w700,
                            fontSize: 17,
                          ),
                        ),
                        const SizedBox(height: 2),
                        Text(p.memberNumber, style: const TextStyle(color: AppTheme.textSecondary)),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 24),
            Text('Details', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            _InfoPanel(
              rows: [
                _InfoRow(icon: Icons.phone_iphone_rounded, label: 'Phone', value: p.phoneNumber),
                if (p.email != null) _InfoRow(icon: Icons.mail_outline_rounded, label: 'Email', value: p.email!),
                _InfoRow(icon: Icons.verified_user_outlined, label: 'KYC status', value: p.kycStatus.name),
                _InfoRow(
                  icon: Icons.calendar_today_outlined,
                  label: 'Member since',
                  value: Formatters.date(p.joinedAt),
                ),
              ],
            ),
            const SizedBox(height: 24),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text('Your accounts', style: Theme.of(context).textTheme.titleMedium),
                TextButton(onPressed: () => context.go('/dashboard'), child: const Text('View all')),
              ],
            ),
            const SizedBox(height: 4),
            AsyncValueView(
              value: accounts,
              data: (context, list) => Container(
                decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(20)),
                child: Column(
                  children: [
                    for (var i = 0; i < list.length; i++) ...[
                      ListTile(
                        contentPadding: const EdgeInsets.symmetric(horizontal: 16),
                        leading: Icon(
                          Icons.account_balance_wallet_outlined,
                          color: Theme.of(context).colorScheme.primary,
                        ),
                        title: Text(
                          list[i].kind.label,
                          style: const TextStyle(color: AppTheme.textPrimary, fontWeight: FontWeight.w600),
                        ),
                        subtitle: Text(list[i].accountNumber, style: const TextStyle(color: AppTheme.textSecondary)),
                        trailing: list[i].balanceLocked && !ref.watch(balancePrivacyProvider).hidden
                            ? const Icon(Icons.lock_outline_rounded, color: AppTheme.textSecondary)
                            : BalanceText(
                                list[i].balance,
                                style: const TextStyle(color: AppTheme.textPrimary, fontWeight: FontWeight.w700),
                              ),
                        onTap: () => openStatement(context, ref, list[i]),
                      ),
                      if (i < list.length - 1) const Divider(height: 1, indent: 16, endIndent: 16),
                    ],
                  ],
                ),
              ),
            ),
            const SizedBox(height: 24),
            const _BalancePrivacySettings(),
            const SizedBox(height: 12),
            Container(
              decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(20)),
              child: ListTile(
                contentPadding: const EdgeInsets.symmetric(horizontal: 16),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
                leading: Icon(Icons.storefront_outlined, color: Theme.of(context).colorScheme.primary),
                title: const Text(
                  'Shares marketplace',
                  style: TextStyle(color: AppTheme.textPrimary, fontWeight: FontWeight.w600),
                ),
                subtitle: const Text(
                  'Sell shares to another member, or buy from one',
                  style: TextStyle(color: AppTheme.textSecondary),
                ),
                trailing: const Icon(Icons.chevron_right, color: AppTheme.textSecondary),
                onTap: () => context.push('/shares-marketplace'),
              ),
            ),
            const SizedBox(height: 28),
            OutlinedButton.icon(
              onPressed: () => ref.read(authControllerProvider.notifier).logout(),
              icon: const Icon(Icons.logout),
              label: const Text('Sign out'),
            ),
          ],
        ),
      ),
    );
  }
}

class _InfoPanel extends StatelessWidget {
  const _InfoPanel({required this.rows});
  final List<_InfoRow> rows;

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(20)),
      child: Column(
        children: [
          for (var i = 0; i < rows.length; i++) ...[
            rows[i],
            if (i < rows.length - 1) const Divider(height: 1, indent: 16, endIndent: 16),
          ],
        ],
      ),
    );
  }
}

class _InfoRow extends StatelessWidget {
  const _InfoRow({required this.icon, required this.label, required this.value});
  final IconData icon;
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: 16),
      leading: Icon(icon, color: AppTheme.textSecondary),
      title: Text(label, style: const TextStyle(color: AppTheme.textSecondary, fontSize: 12)),
      subtitle: Text(
        value,
        style: const TextStyle(color: AppTheme.textPrimary, fontWeight: FontWeight.w600),
      ),
    );
  }
}

/// "Require PIN to show balances" plus, once it's on and the phone has a fingerprint enrolled, the option to use the
/// fingerprint instead of typing the PIN.
class _BalancePrivacySettings extends ConsumerWidget {
  const _BalancePrivacySettings();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final privacy = ref.watch(balancePrivacyProvider);
    final notifier = ref.read(balancePrivacyProvider.notifier);
    final accent = Theme.of(context).colorScheme.primary;
    final label = privacy.biometricLabel;

    return Container(
      decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(20)),
      child: AnimatedSize(
        duration: MediaQuery.disableAnimationsOf(context) ? Duration.zero : const Duration(milliseconds: 250),
        curve: Curves.easeOutCubic,
        alignment: Alignment.topCenter,
        child: Column(
          children: [
            SwitchListTile(
              contentPadding: const EdgeInsets.symmetric(horizontal: 16),
              shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
              secondary: Icon(Icons.visibility_off_outlined, color: accent),
              title: const Text(
                'Require PIN to show balances',
                style: TextStyle(color: AppTheme.textPrimary, fontWeight: FontWeight.w600),
              ),
              subtitle: const Text(
                'Balances stay hidden on this phone until you enter your PIN',
                style: TextStyle(color: AppTheme.textSecondary),
              ),
              value: privacy.requirePin,
              onChanged: (value) async {
                // Turning the guard off is itself protected: otherwise anyone holding the unlocked phone could flip it.
                if (!value && !await confirmPinForPrivacyChange(context, ref)) return;
                await notifier.setRequirePin(value);
              },
            ),
            if (privacy.requirePin && privacy.biometricsAvailable) ...[
              const Divider(height: 1, indent: 16, endIndent: 16),
              SwitchListTile(
                contentPadding: const EdgeInsets.symmetric(horizontal: 16),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
                secondary: Icon(biometricIcon(label), color: accent),
                title: Text(
                  'Use $label to show balances',
                  style: const TextStyle(color: AppTheme.textPrimary, fontWeight: FontWeight.w600),
                ),
                subtitle: const Text(
                  'Skip typing your PIN. Your PIN still works if this doesn\'t',
                  style: TextStyle(color: AppTheme.textSecondary),
                ),
                value: privacy.useBiometrics,
                onChanged: (value) async {
                  // Adding a way in takes the PIN first; removing one doesn't need to.
                  if (value && !await confirmPinForPrivacyChange(context, ref)) return;
                  final saved = await notifier.setUseBiometrics(value);
                  if (!saved && context.mounted) {
                    ScaffoldMessenger.of(context).showSnackBar(
                      SnackBar(content: Text("Your $label wasn't confirmed, so it hasn't been turned on.")),
                    );
                  }
                },
              ),
            ],
          ],
        ),
      ),
    );
  }
}
