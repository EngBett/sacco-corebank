import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:iconsax_flutter/iconsax_flutter.dart';
import 'package:pin_code_fields/pin_code_fields.dart';

import '../../../../core/theme/app_theme.dart';
import '../../../../core/widgets/motion.dart';
import '../../domain/entities/auth_exceptions.dart';
import '../providers/auth_controller.dart';
import '../widgets/auth_illustration.dart';

/// Second step of sign-in on a device that hasn't completed SMS OTP before (ADR 0008).
/// [phone]/[pin] are kept only in memory for this screen's lifetime — never persisted.
class OtpPage extends ConsumerStatefulWidget {
  const OtpPage({super.key, required this.phone, required this.pin, required this.expiresInSeconds});

  final String phone;
  final String pin;
  final int expiresInSeconds;

  @override
  ConsumerState<OtpPage> createState() => _OtpPageState();
}

class _OtpPageState extends ConsumerState<OtpPage> {
  final _codeController = TextEditingController();
  bool _submitting = false;
  bool _verified = false;
  bool _resending = false;
  String _code = '';
  String? _errorText;
  late int _secondsRemaining;
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _secondsRemaining = widget.expiresInSeconds;
    _startCountdown();
  }

  void _startCountdown() {
    _timer?.cancel();
    _timer = Timer.periodic(const Duration(seconds: 1), (timer) {
      if (!mounted) return;
      if (_secondsRemaining <= 1) {
        timer.cancel();
        setState(() => _secondsRemaining = 0);
      } else {
        setState(() => _secondsRemaining--);
      }
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    _codeController.dispose();
    super.dispose();
  }

  Future<void> _verify(String code) async {
    if (code.length != 6 || _submitting) return;
    setState(() {
      _submitting = true;
      _errorText = null;
    });
    try {
      await ref.read(authControllerProvider.notifier).signIn(phone: widget.phone, pin: widget.pin, otp: code);
      // Router redirects to /dashboard once AuthController's state flips to authenticated; the check mark shows until then.
      if (mounted) setState(() => _verified = true);
    } on InvalidOtpException {
      setState(() {
        _errorText = 'That code is incorrect or has expired.';
        _code = '';
      });
    } catch (_) {
      setState(() => _errorText = "Couldn't verify that code. Check your connection and try again.");
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
    // pin_code_fields ignores controller changes while the field is disabled, so the rejected code is cleared only
    // after the rebuild above has re-enabled it; otherwise the digits stay visible with Verify disabled.
    if (mounted && _code.isEmpty) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) _codeController.clear();
      });
    }
  }

  Future<void> _resend() async {
    setState(() {
      _resending = true;
      _errorText = null;
    });
    try {
      final outcome = await ref.read(authControllerProvider.notifier).requestOtp(widget.phone, widget.pin);
      if (!mounted) return;
      setState(() => _secondsRemaining = outcome.expiresInSeconds);
      _startCountdown();
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('A new code was sent.')));
    } catch (_) {
      if (mounted) setState(() => _errorText = "Couldn't resend the code. Try again shortly.");
    } finally {
      if (mounted) setState(() => _resending = false);
    }
  }

  static const _scenes = [
    AuthScene(icon: Iconsax.message_text_copy, badges: [Iconsax.sms_copy, Iconsax.tick_circle_copy]),
    AuthScene(icon: Iconsax.mobile_copy, badges: [Iconsax.message_notif_copy, Iconsax.lock_1_copy]),
    AuthScene(icon: Iconsax.shield_tick_copy, badges: [Iconsax.password_check_copy, Iconsax.finger_scan_copy]),
  ];

  @override
  Widget build(BuildContext context) {
    final accent = Theme.of(context).colorScheme.primary;
    final maskedPhone = widget.phone.length > 4
        ? '••••${widget.phone.substring(widget.phone.length - 4)}'
        : widget.phone;
    final step = FadeSlideIn.step * 2;
    final countdown = '${_secondsRemaining ~/ 60}:${(_secondsRemaining % 60).toString().padLeft(2, '0')}';

    return Scaffold(
      appBar: AppBar(backgroundColor: Colors.transparent),
      body: SafeArea(
        top: false,
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(20, 0, 20, 24),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const AuthIllustration(scenes: _scenes, height: 200),
              const SizedBox(height: 24),
              FadeSlideIn(
                offset: -16,
                child: Semantics(
                  header: true,
                  child: Text(
                    'Verification',
                    textAlign: TextAlign.center,
                    style: Theme.of(context).textTheme.headlineMedium?.copyWith(fontSize: 30),
                  ),
                ),
              ),
              const SizedBox(height: 16),
              FadeSlideIn(
                delay: step,
                offset: -16,
                child: Text(
                  'Please enter the 6-digit code sent to\n$maskedPhone',
                  textAlign: TextAlign.center,
                  style: const TextStyle(fontSize: 16, color: AppTheme.textSecondary, height: 1.5),
                ),
              ),
              const SizedBox(height: 28),
              FadeSlideIn(
                delay: step * 2,
                offset: -16,
                // The app theme fills text fields; the code input's hidden field would paint that fill as a band behind
                // the underlines, so it gets an unfilled theme and a transparent background.
                child: Theme(
                  data: Theme.of(context).copyWith(inputDecorationTheme: const InputDecorationTheme(filled: false)),
                  child: PinCodeTextField(
                    appContext: context,
                    length: 6,
                    controller: _codeController,
                    // This page owns and disposes the controller; the package would dispose it a second time.
                    autoDisposeControllers: false,
                    keyboardType: TextInputType.number,
                    backgroundColor: Colors.transparent,
                    animationType: AnimationType.fade,
                    enabled: !_submitting && !_verified,
                    autoFocus: true,
                    cursorColor: accent,
                    textStyle: AppTheme.money(size: 24, weight: FontWeight.w700),
                    pinTheme: PinTheme(
                      shape: PinCodeFieldShape.underline,
                      fieldHeight: 52,
                      fieldWidth: 40,
                      borderWidth: 2,
                      activeColor: AppTheme.textPrimary,
                      selectedColor: accent,
                      inactiveColor: AppTheme.border,
                      disabledColor: AppTheme.border,
                      activeFillColor: Colors.transparent,
                      selectedFillColor: Colors.transparent,
                      inactiveFillColor: Colors.transparent,
                    ),
                    onChanged: (value) => setState(() => _code = value),
                    onCompleted: _verify,
                  ),
                ),
              ),
              if (_errorText != null)
                Padding(
                  padding: const EdgeInsets.only(top: 4),
                  child: Semantics(
                    liveRegion: true,
                    child: Text(
                      _errorText!,
                      style: const TextStyle(color: AppTheme.negative),
                      textAlign: TextAlign.center,
                    ),
                  ),
                ),
              const SizedBox(height: 12),
              FadeSlideIn(
                delay: step * 3,
                offset: -16,
                child: Wrap(
                  alignment: WrapAlignment.center,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  children: [
                    const Text("Didn't get the code?", style: TextStyle(color: AppTheme.textSecondary)),
                    // An unexpired code is reused server-side rather than re-sent, so resending opens once it expires.
                    TextButton(
                      onPressed: _secondsRemaining > 0 || _resending ? null : _resend,
                      child: Text(
                        _resending
                            ? 'Sending…'
                            : _secondsRemaining > 0
                            ? 'Resend in $countdown'
                            : 'Resend',
                        style: const TextStyle(fontFeatures: AppTheme.moneyFigures),
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 32),
              FadeSlideIn(
                delay: step * 4,
                offset: -16,
                child: FilledButton(
                  onPressed: _code.length < 6 || _submitting || _verified ? null : () => _verify(_code),
                  child: _submitting
                      ? const SizedBox(
                          width: 20,
                          height: 20,
                          child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white),
                        )
                      : _verified
                      ? const Icon(Icons.check_circle_rounded, size: 26, semanticLabel: 'Verified')
                      : const Text('Verify'),
                ),
              ),
              const SizedBox(height: 16),
              const Text(
                "You'll only need to do this once on this phone.",
                textAlign: TextAlign.center,
                style: TextStyle(color: AppTheme.textSecondary, fontSize: 13),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
