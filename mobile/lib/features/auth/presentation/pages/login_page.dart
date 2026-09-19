import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:iconsax_flutter/iconsax_flutter.dart';

import '../../../../core/theme/app_theme.dart';
import '../../../../core/widgets/motion.dart';
import '../../../branding/presentation/providers/branding_providers.dart';
import '../../domain/entities/auth_exceptions.dart';
import '../providers/auth_controller.dart';
import '../widgets/auth_illustration.dart';
import '../widgets/auth_widgets.dart';

class LoginPage extends ConsumerStatefulWidget {
  const LoginPage({super.key});

  @override
  ConsumerState<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends ConsumerState<LoginPage> {
  final _formKey = GlobalKey<FormState>();
  final _phoneController = TextEditingController();
  final _pinController = TextEditingController();
  bool _submitting = false;
  bool _pinHidden = true;
  String? _errorText;

  @override
  void initState() {
    super.initState();
    ref.read(authControllerProvider.notifier).lastUsedPhone().then((phone) {
      if (phone != null && mounted) setState(() => _phoneController.text = phone);
    });
  }

  @override
  void dispose() {
    _phoneController.dispose();
    _pinController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    final phone = _phoneController.text.trim();
    final pin = _pinController.text.trim();

    setState(() {
      _submitting = true;
      _errorText = null;
    });
    try {
      final controller = ref.read(authControllerProvider.notifier);
      final outcome = await controller.requestOtp(phone, pin);
      if (!mounted) return;
      if (outcome.otpRequired) {
        context.push('/otp', extra: {'phone': phone, 'pin': pin, 'expiresInSeconds': outcome.expiresInSeconds});
      } else {
        await controller.signIn(phone: phone, pin: pin);
      }
    } on InvalidCredentialsException {
      setState(() => _errorText = 'Incorrect phone number or PIN.');
    } catch (_) {
      setState(() => _errorText = "Couldn't reach the server. Check your connection and try again.");
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  static const _scenes = [
    AuthScene(icon: Iconsax.wallet_money_copy, badges: [Iconsax.trend_up_copy, Iconsax.money_recive_copy]),
    AuthScene(icon: Iconsax.security_safe_copy, badges: [Iconsax.finger_scan_copy, Iconsax.lock_1_copy]),
    AuthScene(icon: Iconsax.mobile_copy, badges: [Iconsax.card_copy, Iconsax.tick_circle_copy]),
    AuthScene(icon: Iconsax.chart_2_copy, badges: [Iconsax.empty_wallet_add_copy, Iconsax.trend_up_copy]),
  ];

  void _forgotPin(String? supportPhone) => showAuthInfoSheet(
    context,
    icon: Iconsax.password_check_copy,
    title: 'Forgot your PIN?',
    body:
        'For your security, PINs are reset in person. Visit any branch with your national ID'
        '${supportPhone == null || supportPhone.isEmpty ? '' : ', or call $supportPhone'} and staff will set a new one.',
  );

  void _newToSelfService() => showAuthInfoSheet(
    context,
    icon: Iconsax.user_add_copy,
    title: 'Get started',
    body:
        'Self-service is switched on for existing members at the branch. Ask a staff member to enable it and set your '
        'PIN, then sign in here with the phone number on your membership.',
  );

  @override
  Widget build(BuildContext context) {
    final branding = ref.watch(tenantBrandingProvider);
    final b = branding.valueOrNull;
    final step = FadeSlideIn.step * 2;

    return Scaffold(
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(20, 16, 20, 16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _Logo(color: Theme.of(context).colorScheme.primary, logoUrl: b?.logoUrl, label: b?.shortName),
              const SizedBox(height: 8),
              const AuthIllustration(scenes: _scenes, height: 230),
              const SizedBox(height: 16),
              FadeSlideIn(
                offset: -16,
                child: Column(
                  children: [
                    Text('Welcome back', style: Theme.of(context).textTheme.headlineSmall, textAlign: TextAlign.center),
                    const SizedBox(height: 6),
                    Text(
                      b == null ? 'Sign in to your SACCO account' : 'Sign in to ${b.name}',
                      style: const TextStyle(color: AppTheme.textSecondary),
                      textAlign: TextAlign.center,
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 28),
              Form(
                key: _formKey,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    FadeSlideIn(
                      delay: step,
                      offset: -16,
                      child: TextFormField(
                        controller: _phoneController,
                        keyboardType: TextInputType.phone,
                        textInputAction: TextInputAction.next,
                        autofillHints: const [AutofillHints.telephoneNumber],
                        decoration: authFieldDecoration(
                          context,
                          label: 'Phone number',
                          hint: '07XXXXXXXX',
                          icon: Iconsax.call_copy,
                        ),
                        validator: (value) =>
                            (value == null || value.trim().length < 9) ? 'Enter your phone number' : null,
                      ),
                    ),
                    const SizedBox(height: 20),
                    FadeSlideIn(
                      delay: step * 2,
                      offset: -16,
                      child: TextFormField(
                        controller: _pinController,
                        keyboardType: TextInputType.number,
                        obscureText: _pinHidden,
                        maxLength: 6,
                        decoration: authFieldDecoration(
                          context,
                          label: 'PIN',
                          icon: Iconsax.key_copy,
                          suffix: IconButton(
                            tooltip: _pinHidden ? 'Show PIN' : 'Hide PIN',
                            icon: Icon(
                              _pinHidden ? Iconsax.eye_copy : Iconsax.eye_slash_copy,
                              size: 20,
                              color: AppTheme.textSecondary,
                            ),
                            onPressed: () => setState(() => _pinHidden = !_pinHidden),
                          ),
                        ),
                        validator: (value) => (value == null || value.length < 4) ? 'Enter your PIN' : null,
                        onFieldSubmitted: (_) => _submit(),
                      ),
                    ),
                    Align(
                      alignment: Alignment.centerRight,
                      child: TextButton(onPressed: () => _forgotPin(b?.supportPhone), child: const Text('Forgot PIN?')),
                    ),
                    if (_errorText != null)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 12),
                        child: Semantics(
                          liveRegion: true,
                          child: Text(
                            _errorText!,
                            style: const TextStyle(color: AppTheme.negative),
                            textAlign: TextAlign.center,
                          ),
                        ),
                      ),
                    const SizedBox(height: 8),
                    FadeSlideIn(
                      delay: step * 3,
                      offset: -16,
                      child: FilledButton(
                        onPressed: _submitting ? null : _submit,
                        child: _submitting
                            ? const SizedBox(
                                height: 20,
                                width: 20,
                                child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                              )
                            : const Text('Sign in'),
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 20),
              FadeSlideIn(
                delay: step * 4,
                offset: -16,
                child: Wrap(
                  alignment: WrapAlignment.center,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  children: [
                    const Text('New to self-service?', style: TextStyle(color: AppTheme.textSecondary)),
                    TextButton(onPressed: _newToSelfService, child: const Text('Get started')),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Logo extends StatelessWidget {
  const _Logo({required this.color, this.logoUrl, this.label});

  final Color color;
  final String? logoUrl;
  final String? label;

  @override
  Widget build(BuildContext context) {
    final fallback = Center(
      child: Container(
        width: 56,
        height: 56,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          gradient: LinearGradient(
            begin: Alignment.topLeft,
            end: Alignment.bottomRight,
            colors: [color, Color.lerp(color, Colors.black, 0.4)!],
          ),
        ),
        child: const Icon(Icons.account_balance_rounded, color: Colors.white, size: 28),
      ),
    );
    if (logoUrl == null) return fallback;

    // Transparent-background logos are drawn for light backgrounds. On this dark canvas the logo is tinted white
    // (srcIn keeps its shape and cut-outs), the same as using the white transparent variant, and the theme is untouched.
    return Center(
      child: Semantics(
        image: true,
        label: '${label ?? 'SACCO'} logo',
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 180),
          child: Image.network(
            logoUrl!,
            height: 44,
            fit: BoxFit.contain,
            color: AppTheme.textPrimary,
            colorBlendMode: BlendMode.srcIn,
            excludeFromSemantics: true,
            errorBuilder: (_, __, ___) => fallback,
          ),
        ),
      ),
    );
  }
}
