import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

/// Dark wallet look, from the design references (the Flutter-examples wallet and expenses screens): near-black
/// canvas, one step lighter surfaces, blue-grey secondary text, dashed chart grids, 14-20px radii. The app commits to
/// this look regardless of system theme. The tenant's branding colour (`/api/public/tenant/branding`) is the accent
/// everywhere — this app is multi-tenant, so "brand teal" in a reference is "whatever this SACCO's colour is" here.
class AppTheme {
  const AppTheme._();

  static const _fallbackAccent = Color(0xFF02D39A);

  static const background = Color(0xFF0E1117);
  static const surface = Color(0xFF161B22);
  static const surfaceRaised = Color(0xFF1F2630);
  static const border = Color(0xFF2A3441);
  static const textPrimary = Color(0xFFF0F3F8);

  /// Blue-grey secondary text: 6.9:1 on [background], 6.3:1 on [surface].
  static const textSecondary = Color(0xFF8B98A9);
  static const positive = Color(0xFF3DDC97);
  static const negative = Color(0xFFFF6B7A);
  static const warning = Color(0xFFFFB86C);

  /// Second stop for chart lines and hero gradients, as in the wallet reference.
  static const chartCyan = Color(0xFF23B6E6);
  static const gridLine = Color(0x3337434D);

  static const radiusSm = 10.0;
  static const radiusMd = 15.0;
  static const radiusLg = 20.0;
  static const radiusXl = 36.0;

  /// Raleway's default figures are old-style (digits dip below the baseline). Money needs lining, tabular figures so
  /// amounts read as numbers and line up in columns.
  static const moneyFigures = [FontFeature.liningFigures(), FontFeature.tabularFigures()];

  static TextStyle money({double size = 16, FontWeight weight = FontWeight.w700, Color color = textPrimary}) =>
      TextStyle(fontSize: size, fontWeight: weight, color: color, fontFeatures: moneyFigures, letterSpacing: -0.2);

  static ThemeData dark({String? primaryHex}) {
    final accent = _parseHex(primaryHex) ?? _fallbackAccent;
    final onAccent = _readableOn(accent);

    final colorScheme = ColorScheme.fromSeed(seedColor: accent, brightness: Brightness.dark).copyWith(
      primary: accent,
      onPrimary: onAccent,
      secondary: accent,
      surface: surface,
      onSurface: textPrimary,
      error: negative,
    );

    final ralewayFamily = GoogleFonts.raleway().fontFamily;
    final textTheme = GoogleFonts.ralewayTextTheme(
      const TextTheme(
        headlineSmall: TextStyle(color: textPrimary, fontWeight: FontWeight.w700),
        headlineMedium: TextStyle(color: textPrimary, fontWeight: FontWeight.w700),
        titleLarge: TextStyle(color: textPrimary, fontWeight: FontWeight.w700),
        titleMedium: TextStyle(color: textPrimary, fontWeight: FontWeight.w600),
        titleSmall: TextStyle(color: textPrimary, fontWeight: FontWeight.w600),
        bodyLarge: TextStyle(color: textPrimary),
        bodyMedium: TextStyle(color: textPrimary),
        bodySmall: TextStyle(color: textSecondary),
        labelLarge: TextStyle(color: textPrimary, fontWeight: FontWeight.w600),
      ),
    );

    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,
      colorScheme: colorScheme,
      scaffoldBackgroundColor: background,
      canvasColor: background,
      fontFamily: ralewayFamily,
      textTheme: textTheme,
      appBarTheme: AppBarTheme(
        centerTitle: false,
        elevation: 0,
        backgroundColor: background,
        surfaceTintColor: Colors.transparent,
        foregroundColor: textPrimary,
        titleTextStyle: GoogleFonts.raleway(color: textPrimary, fontSize: 20, fontWeight: FontWeight.w700),
        iconTheme: const IconThemeData(color: textPrimary),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: surface,
        hintStyle: GoogleFonts.raleway(color: textSecondary),
        labelStyle: GoogleFonts.raleway(color: textSecondary),
        border: OutlineInputBorder(borderRadius: BorderRadius.circular(radiusMd), borderSide: BorderSide.none),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(radiusMd),
          borderSide: const BorderSide(color: border),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(radiusMd),
          borderSide: BorderSide(color: accent, width: 1.5),
        ),
      ),
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          backgroundColor: accent,
          foregroundColor: onAccent,
          padding: const EdgeInsets.symmetric(vertical: 16),
          // Buttons keep small corners (10px) — softer than square, clearly not pills.
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(radiusSm)),
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: accent,
          foregroundColor: onAccent,
          padding: const EdgeInsets.symmetric(vertical: 16),
          // Buttons keep small corners (10px) — softer than square, clearly not pills.
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(radiusSm)),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: textPrimary,
          padding: const EdgeInsets.symmetric(vertical: 16),
          side: const BorderSide(color: border),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(radiusSm)),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          foregroundColor: accent,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(radiusSm)),
        ),
      ),
      cardTheme: CardThemeData(
        color: surface,
        elevation: 0,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(radiusLg)),
        margin: EdgeInsets.zero,
      ),
      dividerTheme: const DividerThemeData(color: border, space: 1),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: surface,
        indicatorColor: accent.withValues(alpha: 0.18),
        surfaceTintColor: Colors.transparent,
        labelTextStyle: WidgetStateProperty.resolveWith(
          (states) => GoogleFonts.raleway(
            fontSize: 12,
            fontWeight: FontWeight.w600,
            color: states.contains(WidgetState.selected) ? textPrimary : textSecondary,
          ),
        ),
        iconTheme: WidgetStateProperty.resolveWith(
          (states) => IconThemeData(color: states.contains(WidgetState.selected) ? accent : textSecondary),
        ),
      ),
      snackBarTheme: SnackBarThemeData(
        backgroundColor: surfaceRaised,
        contentTextStyle: GoogleFonts.raleway(color: textPrimary),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
      ),
      progressIndicatorTheme: ProgressIndicatorThemeData(color: accent),
      bottomSheetTheme: const BottomSheetThemeData(
        backgroundColor: surface,
        surfaceTintColor: Colors.transparent,
        showDragHandle: true,
        dragHandleColor: border,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(radiusXl))),
      ),
      dialogTheme: DialogThemeData(
        backgroundColor: surface,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(radiusLg)),
      ),
      chipTheme: ChipThemeData(
        backgroundColor: surfaceRaised,
        labelStyle: GoogleFonts.raleway(color: textPrimary, fontSize: 12),
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
      ),
    );
  }

  static Color? _parseHex(String? hex) {
    if (hex == null || hex.isEmpty) return null;
    var value = hex.replaceFirst('#', '');
    if (value.length == 6) value = 'FF$value';
    final parsed = int.tryParse(value, radix: 16);
    return parsed == null ? null : Color(parsed);
  }

  static Color _readableOn(Color background) => background.computeLuminance() > 0.5 ? Colors.black : Colors.white;
}
